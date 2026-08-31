import { Injectable, OnModuleInit } from '@nestjs/common';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import {
  OperationalBookingCreatedEventSchema,
  BOOKING_CREATED_ROUTING_KEY,
} from '@vietride/contracts';
import { NotificationType } from '../generated/notification-prisma-client';
import { RABBITMQ_PREFETCH_ONE } from './core-events.constants';
import { MessageIdempotencyService } from './message-idempotency.service';
import { NotificationsService } from './notifications.service';
import { createNotificationLogger } from './notification-logger';
import { formatBookingReference } from './notification-display';

const QUEUE_NAME = 'notification:booking-created';

@Injectable()
export class BookingCreatedEventsConsumer implements OnModuleInit {
  private readonly logger = createNotificationLogger(BookingCreatedEventsConsumer.name);

  constructor(
    private readonly consumer: RabbitMqConsumer,
    private readonly idempotency: MessageIdempotencyService,
    private readonly notifications: NotificationsService,
  ) {}

  async onModuleInit(): Promise<void> {
    await this.consumer.subscribe(
      QUEUE_NAME,
      BOOKING_CREATED_ROUTING_KEY,
      (payload, raw) => this.handle(payload, raw),
      {
        prefetch: RABBITMQ_PREFETCH_ONE,
        deadLetter: true,
        maxRetries: 5,
        retryDelayMs: 10_000,
      },
    );
  }

  async handle(payload: unknown, raw: ConsumeMessage): Promise<void> {
    const parsed = OperationalBookingCreatedEventSchema.safeParse(payload);
    const messageId = parsed.success
      ? parsed.data.eventId
      : getMessageId(raw) ?? getPayloadEventId(payload);
    if (!messageId) throw new Error(`MISSING_MESSAGE_ID_${BOOKING_CREATED_ROUTING_KEY}`);
    const processingState = await this.idempotency.begin(
      BOOKING_CREATED_ROUTING_KEY,
      messageId,
      raw.content,
    );
    if (processingState === 'duplicate') return;
    if (processingState === 'locked') {
      throw new Error(`MESSAGE_LOCKED_${BOOKING_CREATED_ROUTING_KEY}_${messageId}`);
    }

    try {
      if (!parsed.success) {
        await this.idempotency.markProcessed(BOOKING_CREATED_ROUTING_KEY, messageId);
        this.logger.warn(
          { messageId, issueCount: parsed.error.issues.length },
          'Dropped malformed booking-created event',
        );
        return;
      }

      const event = parsed.data;
      const recipientUserIds = [event.driverUserId, event.assistantUserId].filter(
        (userId): userId is string => userId !== null,
      );
      await Promise.all(
        [...new Set(recipientUserIds)].map((userId) =>
          this.notifications.createNotification({
            userId,
            type: NotificationType.BOOKING_CREATED,
            title: 'Có vé mới trên chuyến',
            body: `Vé ${formatBookingReference(event.bookingCode)} đã được xác nhận cho chuyến của bạn.`,
            data: {
              eventId: event.eventId,
              occurredAt: event.occurredAt,
              bookingId: event.bookingId,
              bookingCode: event.bookingCode,
              tripId: event.tripId,
              status: event.status,
              ticketCodes: event.ticketCodes,
              seatNumbers: event.seatNumbers,
              departureDateTime: event.departureDateTime,
              passengerCount: event.passengerCount,
              pickup: event.pickup,
              dropoff: event.dropoff,
              deepLink: `vietride://driver/trips/${event.tripId}/bookings/${event.bookingId}`,
            },
            dedupeKey: `${BOOKING_CREATED_ROUTING_KEY}:${event.eventId}:${userId}`,
          }),
        ),
      );
      await this.idempotency.markProcessed(BOOKING_CREATED_ROUTING_KEY, messageId);
      this.logger.info(
        { messageId, bookingId: event.bookingId, recipientCount: new Set(recipientUserIds).size },
        'Processed booking-created crew notification',
      );
    } catch (error) {
      await this.idempotency.release(BOOKING_CREATED_ROUTING_KEY, messageId);
      throw error;
    }
  }
}

function getMessageId(raw: ConsumeMessage): string | undefined {
  const { messageId, correlationId } = raw.properties;
  if (typeof messageId === 'string' && messageId.length > 0) return messageId;
  return typeof correlationId === 'string' && correlationId.length > 0 ? correlationId : undefined;
}

function getPayloadEventId(payload: unknown): string | undefined {
  if (typeof payload !== 'object' || payload === null || !('eventId' in payload)) return undefined;
  const eventId = (payload as { eventId?: unknown }).eventId;
  return typeof eventId === 'string' && eventId.length > 0 ? eventId : undefined;
}
