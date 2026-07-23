import { Inject, Injectable, OnModuleInit } from '@nestjs/common';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import { ZodError } from 'zod';
import { EmailTemplateKey } from '../generated/notification-prisma-client';
import { RABBITMQ_PREFETCH_ONE } from './core-events.constants';
import { MessageIdempotencyService } from './message-idempotency.service';
import { NotificationsService } from './notifications.service';
import { createNotificationLogger } from './notification-logger';
import type { OperatorRecipientProvider } from './operator-recipient.provider';
import {
  OPERATOR_RECIPIENT_PROVIDER,
  INVOICE_ISSUED_ROUTING_KEY,
  PARCEL_SUBSCRIPTION_OPERATOR_QUEUE_BINDINGS,
} from './parcel-subscription-operator-events.constants';
import {
  mapParcelSubscriptionOperatorEventToNotifications,
  InvoiceIssuedPayloadSchema,
  type InvoiceIssuedPayload,
  type ParcelSubscriptionOperatorRoutingKey,
} from './parcel-subscription-operator-notification.mapper';

@Injectable()
export class ParcelSubscriptionOperatorEventsConsumer implements OnModuleInit {
  private readonly logger = createNotificationLogger(ParcelSubscriptionOperatorEventsConsumer.name);

  constructor(
    private readonly consumer: RabbitMqConsumer,
    private readonly idempotency: MessageIdempotencyService,
    private readonly notificationsService: NotificationsService,
    @Inject(OPERATOR_RECIPIENT_PROVIDER)
    private readonly operatorRecipientProvider: OperatorRecipientProvider,
  ) {}

  async onModuleInit(): Promise<void> {
    await Promise.all(
      PARCEL_SUBSCRIPTION_OPERATOR_QUEUE_BINDINGS.map((binding) =>
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
    routingKey: ParcelSubscriptionOperatorRoutingKey,
    payload: unknown,
    raw: ConsumeMessage,
  ): Promise<void> {
    const transportMessageId = raw.properties.messageId ?? raw.properties.correlationId;
    const messageId = getCanonicalMessageId(payload) ?? transportMessageId;
    if (!messageId) {
      this.logger.warn(
        { routingKey },
        'Dropping parcel/subscription/operator message without message identity',
      );
      return;
    }

    const processingState = await this.idempotency.begin(routingKey, messageId, raw.content);
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
      let notifications = await mapParcelSubscriptionOperatorEventToNotifications(
        routingKey,
        payload,
        (operatorId) => this.operatorRecipientProvider.resolveOperatorRecipientUserIds(operatorId),
      );
      let invoice: InvoiceIssuedPayload | null = null;
      let invoiceEmailByUserId: ReadonlyMap<string, string> = new Map();
      if (routingKey === INVOICE_ISSUED_ROUTING_KEY) {
        invoice = InvoiceIssuedPayloadSchema.parse(payload);
        const resolveEmails = this.operatorRecipientProvider.resolveOperatorRecipientEmails;
        if (!resolveEmails) {
          throw new Error('OPERATOR_RECIPIENT_EMAIL_PROVIDER_NOT_CONFIGURED');
        }
        const recipientProfiles = await resolveEmails.call(
          this.operatorRecipientProvider,
          invoice.operatorId,
          notifications.map((notification) => notification.userId),
        );
        invoiceEmailByUserId = new Map(
          recipientProfiles.map((recipient) => [recipient.userId, recipient.email]),
        );
        notifications = notifications.filter((notification) =>
          invoiceEmailByUserId.has(notification.userId),
        );
      }
      if (notifications.length === 0) {
        this.logger.warn(
          { routingKey, messageId, recipientCount: 0 },
          'No active operator recipients for parcel/subscription/operator notification event',
        );
        await this.idempotency.markProcessed(routingKey, messageId);
        return;
      }
      const createdNotifications = await Promise.all(
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
      if (invoice) {
        await this.enqueueInvoiceEmails(
          messageId,
          invoice,
          invoiceEmailByUserId,
          createdNotifications,
        );
      }
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

  private async enqueueInvoiceEmails(
    messageId: string,
    invoice: InvoiceIssuedPayload,
    emailByUserId: ReadonlyMap<string, string>,
    notifications: Awaited<ReturnType<NotificationsService['createNotification']>>[],
  ): Promise<void> {
    await Promise.all(
      notifications.map(async (notification) => {
        const toEmail = emailByUserId.get(notification.userId);
        if (!toEmail) {
          throw new Error(`OPERATOR_RECIPIENT_EMAIL_NOT_FOUND_${notification.userId}`);
        }
        await this.notificationsService.enqueueEmail({
          notificationId: notification.id,
          dedupeKey: `${INVOICE_ISSUED_ROUTING_KEY}:${messageId}:${notification.userId}:email`,
          toEmail,
          templateKey: EmailTemplateKey.INVOICE_NOTICE,
          templateData: {
            invoiceNumber: invoice.invoiceNumber,
            amountVnd: invoice.amount,
            invoiceUrl: invoice.invoiceWebUrl,
          },
        });
      }),
    );
  }
}

function getCanonicalMessageId(payload: unknown): string | undefined {
  if (typeof payload !== 'object' || payload === null || !('eventId' in payload)) return undefined;
  const eventId = (payload as { eventId?: unknown }).eventId;
  return typeof eventId === 'string' && eventId.trim().length > 0 ? eventId.trim() : undefined;
}

function buildNotificationDedupeKey(
  routingKey: string,
  messageId: string,
  userId: string,
  type: string,
): string {
  return `${routingKey}:${messageId}:${userId}:${type}`;
}
