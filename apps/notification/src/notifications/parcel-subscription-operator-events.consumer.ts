import { Inject, Injectable, OnModuleInit } from '@nestjs/common';
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
import type { OperatorRecipientProvider } from './operator-recipient.provider';
import {
  OPERATOR_RECIPIENT_PROVIDER,
  PARCEL_SUBSCRIPTION_OPERATOR_QUEUE_BINDINGS,
} from './parcel-subscription-operator-events.constants';
import {
  mapParcelSubscriptionOperatorEventToNotifications,
  type ParcelSubscriptionOperatorRoutingKey,
} from './parcel-subscription-operator-notification.mapper';

@Injectable()
export class ParcelSubscriptionOperatorEventsConsumer implements OnModuleInit {
  private readonly logger = pino({ name: ParcelSubscriptionOperatorEventsConsumer.name });

  constructor(
    private readonly consumer: RabbitMqConsumer,
    private readonly redis: RedisService,
    private readonly notificationsService: NotificationsService,
    @Inject(OPERATOR_RECIPIENT_PROVIDER) private readonly operatorRecipientProvider: OperatorRecipientProvider,
  ) {}

  async onModuleInit(): Promise<void> {
    await Promise.all(
      PARCEL_SUBSCRIPTION_OPERATOR_QUEUE_BINDINGS.map((binding) =>
        this.consumer.subscribe(
          binding.queue,
          binding.routingKey,
          (payload, raw) => this.handle(binding.routingKey, payload, raw),
          { prefetch: RABBITMQ_PREFETCH_ONE },
        ),
      ),
    );
  }

  async handle(
    routingKey: ParcelSubscriptionOperatorRoutingKey,
    payload: unknown,
    raw: ConsumeMessage,
  ): Promise<void> {
    const messageId = raw.properties.messageId ?? raw.properties.correlationId;
    if (!messageId) {
      this.logger.warn({ routingKey }, 'Dropping parcel/subscription/operator message without message id');
      return;
    }

    const isNewMessage = await this.markMessageAsProcessing(routingKey, messageId);
    if (!isNewMessage) {
      this.logger.info({ routingKey, messageId }, 'Skipping duplicate parcel/subscription/operator message');
      return;
    }

    try {
      const notifications = await mapParcelSubscriptionOperatorEventToNotifications(
        routingKey,
        payload,
        (operatorId) => this.operatorRecipientProvider.resolveOperatorRecipientUserIds(operatorId),
      );
      await Promise.all(
        notifications.map((notification) => this.notificationsService.createNotification(notification)),
      );
      this.logger.info(
        { routingKey, messageId, notificationCount: notifications.length },
        'Processed parcel/subscription/operator notification event',
      );
    } catch (error) {
      if (error instanceof ZodError) {
        this.logger.warn(
          { routingKey, messageId, issues: error.issues },
          'Dropping malformed parcel/subscription/operator notification event',
        );
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

