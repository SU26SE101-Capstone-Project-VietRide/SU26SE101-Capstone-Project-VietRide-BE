import { Injectable, OnModuleInit } from '@nestjs/common';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import pino from 'pino';
import { ZodError } from 'zod';
import { mapCoreEventToNotification, type CoreEventRoutingKey } from './core-event-notification.mapper';
import {
  CORE_EVENT_QUEUE_BINDINGS,
  RABBITMQ_PREFETCH_ONE,
} from './core-events.constants';
import { MessageIdempotencyService } from './message-idempotency.service';
import { NotificationsService } from './notifications.service';

@Injectable()
export class CoreEventsConsumer implements OnModuleInit {
  private readonly logger = pino({ name: CoreEventsConsumer.name });

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
          { prefetch: RABBITMQ_PREFETCH_ONE, requeueOnError: true },
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

    const processingState = await this.idempotency.begin(routingKey, messageId);
    if (processingState !== 'acquired') {
      this.logger.info({ routingKey, messageId, processingState }, 'Skipping already handled message');
      return;
    }

    try {
      const notification = mapCoreEventToNotification(routingKey, payload);
      await this.notificationsService.createNotification(notification);
      await this.idempotency.markProcessed(routingKey, messageId);
      this.logger.info({ routingKey, messageId, userId: notification.userId }, 'Processed core notification event');
    } catch (error) {
      if (error instanceof ZodError) {
        this.logger.warn({ routingKey, messageId, issues: error.issues }, 'Dropping malformed notification event');
        await this.idempotency.markProcessed(routingKey, messageId);
        return;
      }

      await this.idempotency.release(routingKey, messageId);
      throw error;
    }
  }
}
