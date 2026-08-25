import { Inject, Injectable, OnModuleInit } from '@nestjs/common';
import {
  BOOKING_PENDING_ACTION_AUTO_RESOLVED_ROUTING_KEY,
  BOOKING_PENDING_ACTION_REALERTED_ROUTING_KEY,
  BOOKING_ROUTE_CHANGE_AUTO_FALLBACK_APPLIED_ROUTING_KEY,
  BOOKING_SCHEDULE_CHANGE_INFORMATIONAL_ROUTING_KEY,
  BOOKING_SCHEDULE_CHANGE_REQUIRED_ROUTING_KEY,
  BOOKING_SEAT_REASSIGNMENT_REQUIRED_ROUTING_KEY,
  BOOKING_SEAT_SHORTAGE_DETECTED_ROUTING_KEY,
  BOOKING_TRANSFER_ESCALATED_ROUTING_KEY,
  BOOKING_TRANSFERRED_ROUTING_KEY,
  BookingSeatShortageDetectedEventSchema,
  BookingTransferEscalatedEventSchema,
  BookingTransferredEventSchema,
} from '@vietride/contracts';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import pino from 'pino';
import { ZodError } from 'zod';
import {
  mapBookingTripChangeToNotification,
  type BookingTripChangeRoutingKey,
} from './booking-trip-change-notification.mapper';
import { RABBITMQ_PREFETCH_ONE } from './core-events.constants';
import { MessageIdempotencyService } from './message-idempotency.service';
import { NotificationsService } from './notifications.service';
import type { OperatorRecipientProvider } from './operator-recipient.provider';
import { OPERATOR_RECIPIENT_PROVIDER } from './parcel-subscription-operator-events.constants';

export const BOOKING_TRIP_CHANGE_QUEUE_BINDINGS = [
  {
    queue: 'notification:booking-seat-reassignment-required',
    routingKey: BOOKING_SEAT_REASSIGNMENT_REQUIRED_ROUTING_KEY,
  },
  {
    queue: 'notification:booking-schedule-change-informational',
    routingKey: BOOKING_SCHEDULE_CHANGE_INFORMATIONAL_ROUTING_KEY,
  },
  {
    queue: 'notification:booking-schedule-change-required',
    routingKey: BOOKING_SCHEDULE_CHANGE_REQUIRED_ROUTING_KEY,
  },
  {
    queue: 'notification:booking-pending-action-realerted',
    routingKey: BOOKING_PENDING_ACTION_REALERTED_ROUTING_KEY,
  },
  {
    queue: 'notification:booking-pending-action-auto-resolved',
    routingKey: BOOKING_PENDING_ACTION_AUTO_RESOLVED_ROUTING_KEY,
  },
  {
    queue: 'notification:booking-route-change-auto-fallback-applied',
    routingKey: BOOKING_ROUTE_CHANGE_AUTO_FALLBACK_APPLIED_ROUTING_KEY,
  },
  {
    queue: 'notification:booking-transferred',
    routingKey: BOOKING_TRANSFERRED_ROUTING_KEY,
  },
  {
    queue: 'notification:booking-seat-shortage-detected',
    routingKey: BOOKING_SEAT_SHORTAGE_DETECTED_ROUTING_KEY,
  },
  {
    queue: 'notification:booking-transfer-escalated',
    routingKey: BOOKING_TRANSFER_ESCALATED_ROUTING_KEY,
  },
] as const;

@Injectable()
export class BookingTripChangeEventsConsumer implements OnModuleInit {
  private readonly logger = pino({ name: BookingTripChangeEventsConsumer.name });

  constructor(
    private readonly consumer: RabbitMqConsumer,
    private readonly idempotency: MessageIdempotencyService,
    private readonly notificationsService: NotificationsService,
    @Inject(OPERATOR_RECIPIENT_PROVIDER)
    private readonly operatorRecipientProvider: OperatorRecipientProvider = {
      resolveOperatorRecipientUserIds: async () => [],
    },
  ) {}

  async onModuleInit(): Promise<void> {
    await Promise.all(
      BOOKING_TRIP_CHANGE_QUEUE_BINDINGS.map((binding) =>
        this.consumer.subscribe(
          binding.queue,
          binding.routingKey,
          (payload, raw) => this.handle(binding.routingKey, payload, raw),
          {
            prefetch: RABBITMQ_PREFETCH_ONE,
            deadLetter: true,
            maxRetries: 5,
            retryDelayMs: 10_000,
          },
        ),
      ),
    );
  }

  async handle(
    routingKey: BookingTripChangeRoutingKey,
    payload: unknown,
    raw: ConsumeMessage,
  ): Promise<void> {
    const messageId = getMessageId(raw, routingKey === BOOKING_TRANSFERRED_ROUTING_KEY);
    if (!messageId) {
      throw new Error(`MISSING_MESSAGE_ID_${routingKey}`);
    }

    const processingState = await this.idempotency.begin(routingKey, messageId, raw.content);
    if (processingState === 'duplicate') {
      this.logger.info(
        { routingKey, messageId, processingState },
        'Skipping already handled Booking trip-change message',
      );
      return;
    }
    if (processingState === 'locked') {
      throw new Error(`MESSAGE_LOCKED_${routingKey}_${messageId}`);
    }

    try {
      if (routingKey === BOOKING_TRANSFERRED_ROUTING_KEY) {
        const transferred = BookingTransferredEventSchema.parse(payload);
        const requiresNotification = transferred.notifyPassengers
          || transferred.transfers.some(
            (transfer) =>
              transfer.newSeatNumber === null
              || transfer.originalBoardingStatus === 'PENDING'
              || (transfer.originalBoardingStatus === undefined
                && transfer.confirmationStatus === 'NOT_REQUIRED'),
          );
        if (!requiresNotification) {
          await this.idempotency.markProcessed(routingKey, messageId);
          return;
        }
      }

      const operatorPayload = routingKey === BOOKING_SEAT_SHORTAGE_DETECTED_ROUTING_KEY
        ? BookingSeatShortageDetectedEventSchema.parse(payload)
        : routingKey === BOOKING_TRANSFER_ESCALATED_ROUTING_KEY
          ? BookingTransferEscalatedEventSchema.parse(payload)
          : undefined;
      const operatorRecipients = operatorPayload
        ? await this.operatorRecipientProvider.resolveOperatorRecipientUserIds(
          operatorPayload.operatorId,
        )
        : [];
      const notifications = operatorPayload
        ? [...new Set(operatorRecipients)].map((userId) =>
          mapBookingTripChangeToNotification(routingKey, operatorPayload, userId))
        : [mapBookingTripChangeToNotification(routingKey, payload)];
      await Promise.all(notifications.map((notification) =>
        this.notificationsService.createNotification({
          ...notification,
          dedupeKey: `${routingKey}:${messageId}:${notification.userId}:${notification.type}`,
        })));
      await this.idempotency.markProcessed(routingKey, messageId);
      this.logger.info(
        { routingKey, messageId, recipientCount: notifications.length },
        'Processed Booking trip-change notification event',
      );
    } catch (error) {
      if (error instanceof ZodError) {
        this.logger.warn(
          { routingKey, messageId, issues: error.issues },
          'Dropping malformed Booking trip-change notification event',
        );
        await this.idempotency.markProcessed(routingKey, messageId);
        return;
      }

      await this.idempotency.release(routingKey, messageId);
      throw error;
    }
  }
}

function getMessageId(raw: ConsumeMessage, requireMessageId = false): string | undefined {
  const properties: unknown = raw.properties;
  if (typeof properties !== 'object' || properties === null) return undefined;

  const { messageId, correlationId } = properties as Record<string, unknown>;
  if (typeof messageId === 'string') return messageId;
  if (requireMessageId) return undefined;
  return typeof correlationId === 'string' ? correlationId : undefined;
}
