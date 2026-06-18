import { Inject, Injectable, OnModuleInit } from '@nestjs/common';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import pino from 'pino';
import { ZodError } from 'zod';
import {
  RABBITMQ_PREFETCH_ONE,
} from './core-events.constants';
import { MessageIdempotencyService } from './message-idempotency.service';
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
    private readonly idempotency: MessageIdempotencyService,
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
          { prefetch: RABBITMQ_PREFETCH_ONE, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000 },
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
      throw new Error(`MISSING_MESSAGE_ID_${routingKey}`);
    }

    const processingState = await this.idempotency.begin(routingKey, messageId);
    if (processingState === 'duplicate') {
      this.logger.info(
        { routingKey, messageId, processingState },
        'Skipping already handled parcel/subscription/operator message',
      );
      return;
    }
    if (processingState === 'locked') {
      throw new Error(`MESSAGE_LOCKED_${routingKey}_${messageId}`);
    }

    try {
      const notifications = await mapParcelSubscriptionOperatorEventToNotifications(
        routingKey,
        payload,
        (operatorId) => this.operatorRecipientProvider.resolveOperatorRecipientUserIds(operatorId),
      );
      if (notifications.length === 0) {
        this.logger.warn(
          { routingKey, messageId, recipientCount: 0 },
          'No active operator recipients for parcel/subscription/operator notification event',
        );
        await this.idempotency.markProcessed(routingKey, messageId);
        return;
      }
      await Promise.all(
        notifications.map((notification) =>
          this.notificationsService.createNotification({
            ...notification,
            dedupeKey: buildNotificationDedupeKey(
              routingKey,
              messageId,
              notification.userId,
              notification.type,
            ),
          }),
        ),
      );
      await this.idempotency.markProcessed(routingKey, messageId);
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
        await this.idempotency.markProcessed(routingKey, messageId);
        return;
      }

      await this.idempotency.release(routingKey, messageId);
      throw error;
    }
  }
}

function buildNotificationDedupeKey(
  routingKey: string,
  messageId: string,
  userId: string,
  type: string,
): string {
  return `${routingKey}:${messageId}:${userId}:${type}`;
}
