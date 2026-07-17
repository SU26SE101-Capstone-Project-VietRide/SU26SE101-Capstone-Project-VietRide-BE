import { Injectable, OnModuleInit } from '@nestjs/common';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import { ZodError } from 'zod';
import { BookingCancelledConsumerEventSchema } from '@vietride/contracts';
import {
  mapCoreEventToNotification,
  type CoreEventRoutingKey,
} from './core-event-notification.mapper';
import {
  BOOKING_CANCELLED_ROUTING_KEY,
  CORE_EVENT_QUEUE_BINDINGS,
  RABBITMQ_PREFETCH_ONE,
} from './core-events.constants';
import { MessageIdempotencyService } from './message-idempotency.service';
import { NotificationsService } from './notifications.service';
import { createNotificationLogger } from './notification-logger';

@Injectable()
export class CoreEventsConsumer implements OnModuleInit {
  private readonly logger = createNotificationLogger(CoreEventsConsumer.name);

  constructor(
    private readonly consumer: RabbitMqConsumer,
    private readonly idempotency: MessageIdempotencyService,
    private readonly notificationsService: NotificationsService,
  ) {}

  async onModuleInit(): Promise<void> {
    await Promise.all(
      CORE_EVENT_QUEUE_BINDINGS.map((binding) =>
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
    routingKey: CoreEventRoutingKey,
    payload: unknown,
    raw: ConsumeMessage,
  ): Promise<void> {
    const brokerMessageId = getMessageId(raw);
    if (!brokerMessageId) {
      throw new Error(`MISSING_MESSAGE_ID_${routingKey}`);
    }
    const messageId = resolveDedupeIdentity(routingKey, payload, brokerMessageId);

    const processingState = await this.idempotency.begin(routingKey, messageId);
    if (processingState === 'duplicate') {
      this.logger.info(
        { routingKey, messageId, processingState },
        'Skipping already handled message',
      );
      return;
    }
    if (processingState === 'locked') {
      throw new Error(`MESSAGE_LOCKED_${routingKey}_${messageId}`);
    }

    try {
      const notification = mapCoreEventToNotification(routingKey, payload);
      await this.notificationsService.createNotification({
        ...notification,
        dedupeKey: buildNotificationDedupeKey(
          routingKey,
          messageId,
          notification.userId,
          notification.type,
        ),
      });
      await this.idempotency.markProcessed(routingKey, messageId);
      this.logger.info(
        { routingKey, messageId, userId: notification.userId },
        'Processed core notification event',
      );
    } catch (error) {
      if (error instanceof ZodError) {
        this.logger.warn(
          { routingKey, messageId, issues: error.issues },
          'Dropping malformed notification event',
        );
        await this.idempotency.markProcessed(routingKey, messageId);
        return;
      }

      await this.idempotency.release(routingKey, messageId);
      throw error;
    }
  }
}

function resolveDedupeIdentity(
  routingKey: CoreEventRoutingKey,
  payload: unknown,
  brokerMessageId: string,
): string {
  if (routingKey !== BOOKING_CANCELLED_ROUTING_KEY) {
    return brokerMessageId;
  }

  const cancellationEvent = BookingCancelledConsumerEventSchema.parse(payload);
  return 'eventId' in cancellationEvent
    ? cancellationEvent.eventId
    : cancellationEvent.bookingId;
}

function getMessageId(raw: ConsumeMessage): string | undefined {
  const properties: unknown = raw.properties;
  if (typeof properties !== 'object' || properties === null) {
    return undefined;
  }

  const { messageId, correlationId } = properties as Record<string, unknown>;
  if (typeof messageId === 'string') {
    return messageId;
  }

  return typeof correlationId === 'string' ? correlationId : undefined;
}

function buildNotificationDedupeKey(
  routingKey: string,
  messageId: string,
  userId: string,
  type: string,
): string {
  return `${routingKey}:${messageId}:${userId}:${type}`;
}
