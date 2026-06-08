import { Injectable, OnModuleInit } from '@nestjs/common';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import { RedisService } from '@vietride/nest-redis';
import type { ConsumeMessage } from 'amqplib';
import pino from 'pino';
import { ZodError } from 'zod';
import {
  RABBITMQ_IDEMPOTENCY_TTL_SECONDS,
  RABBITMQ_PREFETCH_ONE,
} from './core-events.constants';
import { NotificationsService } from './notifications.service';
import {
  TRIP_TRACKING_ALERT_QUEUE_BINDINGS,
} from './trip-tracking-alert-events.constants';
import {
  mapTripTrackingAlertToNotifications,
  type TripTrackingAlertRoutingKey,
} from './trip-tracking-alert-notification.mapper';

@Injectable()
export class TripTrackingAlertEventsConsumer implements OnModuleInit {
  private readonly logger = pino({ name: TripTrackingAlertEventsConsumer.name });

  constructor(
    private readonly consumer: RabbitMqConsumer,
    private readonly redis: RedisService,
    private readonly notificationsService: NotificationsService,
  ) {}

  async onModuleInit(): Promise<void> {
    await Promise.all(
      TRIP_TRACKING_ALERT_QUEUE_BINDINGS.map((binding) =>
        this.consumer.subscribe(
          binding.queue,
          binding.routingKey,
          (payload, raw) => this.handle(binding.routingKey, payload, raw),
          { prefetch: RABBITMQ_PREFETCH_ONE },
        ),
      ),
    );
  }

  async handle(routingKey: TripTrackingAlertRoutingKey, payload: unknown, raw: ConsumeMessage): Promise<void> {
    const messageId = raw.properties.messageId ?? raw.properties.correlationId;
    if (!messageId) {
      this.logger.warn({ routingKey }, 'Dropping alert message without message id');
      return;
    }

    const isNewMessage = await this.markMessageAsProcessing(routingKey, messageId);
    if (!isNewMessage) {
      this.logger.info({ routingKey, messageId }, 'Skipping duplicate alert message');
      return;
    }

    try {
      const notifications = mapTripTrackingAlertToNotifications(routingKey, payload);
      await Promise.all(
        notifications.map((notification) => this.notificationsService.createNotification(notification)),
      );
      this.logger.info(
        { routingKey, messageId, notificationCount: notifications.length },
        'Processed trip/tracking alert notification event',
      );
    } catch (error) {
      if (error instanceof ZodError) {
        this.logger.warn({ routingKey, messageId, issues: error.issues }, 'Dropping malformed alert notification event');
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
