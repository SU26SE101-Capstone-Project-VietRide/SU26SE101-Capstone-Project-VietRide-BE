import { Injectable, OnModuleInit } from '@nestjs/common';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import pino from 'pino';
import { ZodError } from 'zod';
import {
  RABBITMQ_PREFETCH_ONE,
} from './core-events.constants';
import { MessageIdempotencyService } from './message-idempotency.service';
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
    private readonly idempotency: MessageIdempotencyService,
    private readonly notificationsService: NotificationsService,
  ) {}

  async onModuleInit(): Promise<void> {
    await Promise.all(
      TRIP_TRACKING_ALERT_QUEUE_BINDINGS.map((binding) =>
        this.consumer.subscribe(
          binding.queue,
          binding.routingKey,
          (payload, raw) => this.handle(binding.routingKey, payload, raw),
          { prefetch: RABBITMQ_PREFETCH_ONE, requeueOnError: true },
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

    const processingState = await this.idempotency.begin(routingKey, messageId);
    if (processingState !== 'acquired') {
      this.logger.info({ routingKey, messageId, processingState }, 'Skipping already handled alert message');
      return;
    }

    try {
      const notifications = mapTripTrackingAlertToNotifications(routingKey, payload);
      await Promise.all(
        notifications.map((notification) => this.notificationsService.createNotification(notification)),
      );
      await this.idempotency.markProcessed(routingKey, messageId);
      this.logger.info(
        { routingKey, messageId, notificationCount: notifications.length },
        'Processed trip/tracking alert notification event',
      );
    } catch (error) {
      if (error instanceof ZodError) {
        this.logger.warn({ routingKey, messageId, issues: error.issues }, 'Dropping malformed alert notification event');
        await this.idempotency.markProcessed(routingKey, messageId);
        return;
      }

      await this.idempotency.release(routingKey, messageId);
      throw error;
    }
  }
}
