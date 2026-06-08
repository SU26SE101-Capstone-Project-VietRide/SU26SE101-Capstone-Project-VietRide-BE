import { Injectable, OnModuleInit } from '@nestjs/common';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import { RedisService } from '@vietride/nest-redis';
import type { ConsumeMessage } from 'amqplib';
import pino from 'pino';
import { ZodError } from 'zod';
import { mapCoreEventToNotification, type CoreEventRoutingKey } from './core-event-notification.mapper';
import {
  CORE_EVENT_QUEUE_BINDINGS,
  RABBITMQ_IDEMPOTENCY_TTL_SECONDS,
  RABBITMQ_PREFETCH_ONE,
} from './core-events.constants';
import { NotificationsService } from './notifications.service';

@Injectable()
export class CoreEventsConsumer implements OnModuleInit {
  private readonly logger = pino({ name: CoreEventsConsumer.name });

  constructor(
    private readonly consumer: RabbitMqConsumer,
    private readonly redis: RedisService,
    private readonly notificationsService: NotificationsService,
  ) {}

  async onModuleInit(): Promise<void> {
    await Promise.all(
      CORE_EVENT_QUEUE_BINDINGS.map((binding) =>
        this.consumer.subscribe(
          binding.queue,
          binding.routingKey,
          (payload, raw) => this.handle(binding.routingKey, payload, raw),
          { prefetch: RABBITMQ_PREFETCH_ONE },
        ),
      ),
    );
  }

  async handle(routingKey: CoreEventRoutingKey, payload: unknown, raw: ConsumeMessage): Promise<void> {
    const messageId = raw.properties.messageId ?? raw.properties.correlationId;
    if (!messageId) {
      this.logger.warn({ routingKey }, 'Dropping message without message id');
      return;
    }

    const isNewMessage = await this.markMessageAsProcessing(routingKey, messageId);
    if (!isNewMessage) {
      this.logger.info({ routingKey, messageId }, 'Skipping duplicate message');
      return;
    }

    try {
      const notification = mapCoreEventToNotification(routingKey, payload);
      await this.notificationsService.createNotification(notification);
      this.logger.info({ routingKey, messageId, userId: notification.userId }, 'Processed core notification event');
    } catch (error) {
      if (error instanceof ZodError) {
        this.logger.warn({ routingKey, messageId, issues: error.issues }, 'Dropping malformed notification event');
        return;
      }

      throw error;
    }
  }

  private async markMessageAsProcessing(routingKey: string, messageId: string): Promise<boolean> {
    const key = `notification:idem:${routingKey}:${messageId}`;
    const result = await this.redis
      .getClient()
      .set(key, '1', 'EX', RABBITMQ_IDEMPOTENCY_TTL_SECONDS, 'NX');

    return result === 'OK';
  }
}
