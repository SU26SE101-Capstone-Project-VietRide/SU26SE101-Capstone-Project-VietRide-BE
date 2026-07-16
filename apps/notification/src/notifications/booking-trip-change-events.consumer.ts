import { Injectable, OnModuleInit } from '@nestjs/common';
import {
  BOOKING_PENDING_ACTION_REALERTED_ROUTING_KEY,
  BOOKING_SCHEDULE_CHANGE_INFORMATIONAL_ROUTING_KEY,
  BOOKING_SCHEDULE_CHANGE_REQUIRED_ROUTING_KEY,
  BOOKING_SEAT_REASSIGNMENT_REQUIRED_ROUTING_KEY,
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
] as const;

@Injectable()
export class BookingTripChangeEventsConsumer implements OnModuleInit {
  private readonly logger = pino({ name: BookingTripChangeEventsConsumer.name });

  constructor(
    private readonly consumer: RabbitMqConsumer,
    private readonly idempotency: MessageIdempotencyService,
    private readonly notificationsService: NotificationsService,
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
    const messageId = raw.properties.messageId ?? raw.properties.correlationId;
    if (!messageId) {
      throw new Error(`MISSING_MESSAGE_ID_${routingKey}`);
    }

    const processingState = await this.idempotency.begin(routingKey, messageId);
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
      const notification = mapBookingTripChangeToNotification(routingKey, payload);
      const eventId = readEventId(notification.data);
      await this.notificationsService.createNotification({
        ...notification,
        dedupeKey: `${routingKey}:${eventId}:${notification.userId}:${notification.type}`,
      });
      await this.idempotency.markProcessed(routingKey, messageId);
      this.logger.info(
        { routingKey, messageId, eventId, userId: notification.userId },
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

function readEventId(data: unknown): string {
  if (typeof data !== 'object' || data === null || !('eventId' in data)) {
    throw new Error('BOOKING_TRIP_CHANGE_EVENT_ID_MISSING');
  }

  const eventId = (data as { eventId?: unknown }).eventId;
  if (typeof eventId !== 'string') {
    throw new Error('BOOKING_TRIP_CHANGE_EVENT_ID_MISSING');
  }

  return eventId;
}
