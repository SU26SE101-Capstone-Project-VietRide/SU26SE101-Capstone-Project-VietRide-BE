import { Injectable, OnModuleInit } from '@nestjs/common';
import {
  BOOKING_STOP_DISABLED_AUTO_FALLBACK_APPLIED_ROUTING_KEY,
  BookingStopDisabledAutoFallbackAppliedEventSchema,
} from '@vietride/contracts';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import { RABBITMQ_PREFETCH_ONE } from './core-events.constants';
import { mapStopDisabledAutoFallbackToNotification } from './day24-stop-disabled-auto-fallback-notification.mapper';
import { MessageIdempotencyService } from './message-idempotency.service';
import { createNotificationLogger } from './notification-logger';
import { NotificationsService } from './notifications.service';

export const DAY24_STOP_DISABLED_AUTO_FALLBACK_QUEUE_BINDING = {
  queue: 'notification:booking-stop-disabled-auto-fallback-applied',
  routingKey: BOOKING_STOP_DISABLED_AUTO_FALLBACK_APPLIED_ROUTING_KEY,
} as const;

@Injectable()
export class Day24StopDisabledAutoFallbackEventsConsumer implements OnModuleInit {
  private readonly logger = createNotificationLogger(
    Day24StopDisabledAutoFallbackEventsConsumer.name,
  );

  constructor(
    private readonly consumer: RabbitMqConsumer,
    private readonly idempotency: MessageIdempotencyService,
    private readonly notificationsService: NotificationsService,
  ) {}

  async onModuleInit(): Promise<void> {
    await this.consumer.subscribe(
      DAY24_STOP_DISABLED_AUTO_FALLBACK_QUEUE_BINDING.queue,
      DAY24_STOP_DISABLED_AUTO_FALLBACK_QUEUE_BINDING.routingKey,
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
    const brokerMessageId = getMessageId(raw);
    if (!brokerMessageId) {
      throw new Error(
        `MISSING_MESSAGE_ID_${BOOKING_STOP_DISABLED_AUTO_FALLBACK_APPLIED_ROUTING_KEY}`,
      );
    }

    const parsed = BookingStopDisabledAutoFallbackAppliedEventSchema.safeParse(payload);
    const eventId = parsed.success ? parsed.data.eventId : brokerMessageId;
    const processingState = await this.idempotency.begin(
      BOOKING_STOP_DISABLED_AUTO_FALLBACK_APPLIED_ROUTING_KEY,
      eventId,
      raw.content,
    );
    if (processingState === 'duplicate') return;
    if (processingState === 'locked') {
      throw new Error(
        `MESSAGE_LOCKED_${BOOKING_STOP_DISABLED_AUTO_FALLBACK_APPLIED_ROUTING_KEY}_${eventId}`,
      );
    }

    try {
      if (!parsed.success) {
        this.logger.warn(
          {
            routingKey: BOOKING_STOP_DISABLED_AUTO_FALLBACK_APPLIED_ROUTING_KEY,
            eventId,
            issues: parsed.error.issues,
          },
          'Dropping malformed stop-disabled fallback event',
        );
        await this.idempotency.markProcessed(
          BOOKING_STOP_DISABLED_AUTO_FALLBACK_APPLIED_ROUTING_KEY,
          eventId,
        );
        return;
      }

      const notification = mapStopDisabledAutoFallbackToNotification(parsed.data);
      await this.notificationsService.createNotification({
        ...notification,
        dedupeKey: `${BOOKING_STOP_DISABLED_AUTO_FALLBACK_APPLIED_ROUTING_KEY}:${eventId}:${notification.userId}:${notification.type}`,
      });
      await this.idempotency.markProcessed(
        BOOKING_STOP_DISABLED_AUTO_FALLBACK_APPLIED_ROUTING_KEY,
        eventId,
      );
    } catch (error) {
      await this.idempotency.release(
        BOOKING_STOP_DISABLED_AUTO_FALLBACK_APPLIED_ROUTING_KEY,
        eventId,
      );
      throw error;
    }
  }
}

function getMessageId(raw: ConsumeMessage): string | undefined {
  const properties: unknown = raw.properties;
  if (typeof properties !== 'object' || properties === null) return undefined;

  const { messageId, correlationId } = properties as Record<string, unknown>;
  if (typeof messageId === 'string') return messageId;
  return typeof correlationId === 'string' ? correlationId : undefined;
}
