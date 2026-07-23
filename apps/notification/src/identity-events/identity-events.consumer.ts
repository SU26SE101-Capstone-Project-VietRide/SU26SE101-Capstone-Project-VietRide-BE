import { Inject, Injectable, Logger, OnModuleInit } from '@nestjs/common';
import {
  IDENTITY_OPERATOR_APPROVED_ROUTING_KEY,
  IDENTITY_OPERATOR_SUSPENDED_ROUTING_KEY,
  IDENTITY_OTP_REQUESTED_ROUTING_KEY,
  IDENTITY_USER_CREATED_ROUTING_KEY,
  IdentityOperatorApprovedEventSchema,
  IdentityOperatorSuspendedEventSchema,
  IdentityOtpRequestedEventSchema,
  IdentityUserCreatedEventSchema,
} from '@vietride/contracts';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import { ZodError } from 'zod';
import { EmailTemplateKey, NotificationType } from '../generated/notification-prisma-client';
import { RABBITMQ_PREFETCH_ONE } from '../notifications/core-events.constants';
import { MessageIdempotencyService } from '../notifications/message-idempotency.service';
import { NotificationsService } from '../notifications/notifications.service';
import type { OperatorRecipientProvider } from '../notifications/operator-recipient.provider';
import { OPERATOR_RECIPIENT_PROVIDER } from '../notifications/parcel-subscription-operator-events.constants';

/**
 * Subscribes to Identity lifecycle events. user.created remains a no-op because
 * onboarding ownership belongs to Identity/Payment. Operator lifecycle events
 * produce in-app notifications for active OPERATOR_ADMIN recipients resolved by
 * Identity.
 */
@Injectable()
export class IdentityEventsConsumer implements OnModuleInit {
  private readonly logger = new Logger(IdentityEventsConsumer.name);

  constructor(
    private readonly consumer: RabbitMqConsumer,
    private readonly idempotency: MessageIdempotencyService,
    private readonly notificationsService: NotificationsService,
    @Inject(OPERATOR_RECIPIENT_PROVIDER)
    private readonly operatorRecipientProvider: OperatorRecipientProvider,
  ) {}

  async onModuleInit(): Promise<void> {
    await this.consumer.subscribe(
      'notification.identity.user.created',
      IDENTITY_USER_CREATED_ROUTING_KEY,
      (payload) => {
        const event = IdentityUserCreatedEventSchema.parse(payload);
        this.logger.log(
          `Consumed ${IDENTITY_USER_CREATED_ROUTING_KEY} userId=${event.userId} role=${event.role}`,
        );
      },
      { prefetch: RABBITMQ_PREFETCH_ONE, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000 },
    );

    await this.consumer.subscribe(
      'notification.identity.operator.approved',
      IDENTITY_OPERATOR_APPROVED_ROUTING_KEY,
      (payload, raw) => this.handleOperatorApproved(payload, raw),
      { prefetch: RABBITMQ_PREFETCH_ONE, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000 },
    );

    await this.consumer.subscribe(
      'notification.identity.operator.suspended',
      IDENTITY_OPERATOR_SUSPENDED_ROUTING_KEY,
      (payload, raw) => this.handleOperatorSuspended(payload, raw),
      { prefetch: RABBITMQ_PREFETCH_ONE, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000 },
    );

    await this.consumer.subscribe(
      'notification.identity.otp.requested',
      IDENTITY_OTP_REQUESTED_ROUTING_KEY,
      (payload, raw) => this.handleOtpRequested(payload, raw),
      { prefetch: RABBITMQ_PREFETCH_ONE, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000 },
    );
  }

  async handleOperatorApproved(payload: unknown, raw: ConsumeMessage): Promise<void> {
    await this.handleOperatorLifecycle(
      IDENTITY_OPERATOR_APPROVED_ROUTING_KEY,
      payload,
      raw,
      NotificationType.OPERATOR_APPROVED,
      'Nha xe da duoc duyet',
      'Nha xe cua ban da duoc duyet. Ban co the dang nhap va bat dau van hanh.',
    );
  }

  async handleOperatorSuspended(payload: unknown, raw: ConsumeMessage): Promise<void> {
    await this.handleOperatorLifecycle(
      IDENTITY_OPERATOR_SUSPENDED_ROUTING_KEY,
      payload,
      raw,
      NotificationType.OPERATOR_SUSPENDED,
      'Nha xe bi tam ngung',
      'Nha xe cua ban da bi tam ngung. Vui long lien he quan tri he thong de duoc ho tro.',
    );
  }

  async handleOtpRequested(payload: unknown, raw: ConsumeMessage): Promise<void> {
    const routingKey = IDENTITY_OTP_REQUESTED_ROUTING_KEY;
    const messageId = raw.properties.messageId ?? raw.properties.correlationId;
    if (!messageId) {
      throw new Error(`MISSING_MESSAGE_ID_${routingKey}`);
    }

    const processingState = await this.idempotency.begin(routingKey, messageId, raw.content);
    if (processingState === 'duplicate') {
      this.logger.log(`Skipping already handled ${routingKey} messageId=${messageId}`);
      return;
    }
    if (processingState === 'locked') {
      throw new Error(`MESSAGE_LOCKED_${routingKey}_${messageId}`);
    }

    try {
      const event = IdentityOtpRequestedEventSchema.parse(payload);
      const emailDomain = event.email.split('@')[1] ?? 'unknown';

      await this.notificationsService.enqueueEmail({
        toEmail: event.email,
        templateKey: EmailTemplateKey.AUTH_OTP,
        templateData: { code: event.code, purpose: event.purpose, ttlMinutes: event.ttlMinutes },
      });

      await this.idempotency.markProcessed(routingKey, messageId);
      this.logger.log(
        `Processed ${routingKey} messageId=${messageId} userId=${event.userId} emailDomain=${emailDomain}`,
      );
    } catch (error) {
      if (error instanceof ZodError) {
        this.logger.warn(`Dropping malformed ${routingKey} messageId=${messageId}`);
        await this.idempotency.markProcessed(routingKey, messageId);
        return;
      }

      await this.idempotency.release(routingKey, messageId);
      throw error;
    }
  }

  private async handleOperatorLifecycle(
    routingKey:
      | typeof IDENTITY_OPERATOR_APPROVED_ROUTING_KEY
      | typeof IDENTITY_OPERATOR_SUSPENDED_ROUTING_KEY,
    payload: unknown,
    raw: ConsumeMessage,
    type: NotificationType,
    title: string,
    body: string,
  ): Promise<void> {
    const messageId = raw.properties.messageId ?? raw.properties.correlationId;
    if (!messageId) {
      throw new Error(`MISSING_MESSAGE_ID_${routingKey}`);
    }

    const processingState = await this.idempotency.begin(routingKey, messageId, raw.content);
    if (processingState === 'duplicate') {
      this.logger.log(`Skipping already handled ${routingKey} messageId=${messageId}`);
      return;
    }
    if (processingState === 'locked') {
      throw new Error(`MESSAGE_LOCKED_${routingKey}_${messageId}`);
    }

    try {
      const event =
        routingKey === IDENTITY_OPERATOR_APPROVED_ROUTING_KEY
          ? IdentityOperatorApprovedEventSchema.parse(payload)
          : IdentityOperatorSuspendedEventSchema.parse(payload);
      const recipientUserIds = await this.operatorRecipientProvider.resolveOperatorRecipientUserIds(
        event.operatorId,
      );

      if (recipientUserIds.length === 0) {
        this.logger.warn(
          `No active operator admin recipients for ${routingKey} operatorId=${event.operatorId} messageId=${messageId}`,
        );
        await this.idempotency.markProcessed(routingKey, messageId);
        return;
      }

      await Promise.all(
        recipientUserIds.map((userId) =>
          this.notificationsService.createNotification({
            userId,
            type,
            title,
            body,
            data: buildOperatorLifecycleData(routingKey, event.operatorId, event),
            dedupeKey: buildNotificationDedupeKey(routingKey, messageId, userId, type),
          }),
        ),
      );
      await this.idempotency.markProcessed(routingKey, messageId);
      this.logger.log(
        `Processed ${routingKey} messageId=${messageId} notificationCount=${recipientUserIds.length}`,
      );
    } catch (error) {
      if (error instanceof ZodError) {
        this.logger.warn(`Dropping malformed ${routingKey} messageId=${messageId}`);
        await this.idempotency.markProcessed(routingKey, messageId);
        return;
      }

      await this.idempotency.release(routingKey, messageId);
      throw error;
    }
  }
}

function buildOperatorLifecycleData(
  routingKey: string,
  operatorId: string,
  event: { approvedAt?: string; suspendedAt?: string },
): Record<string, string> {
  if (routingKey === IDENTITY_OPERATOR_APPROVED_ROUTING_KEY) {
    return { operatorId, approvedAt: event.approvedAt ?? '' };
  }

  return { operatorId, suspendedAt: event.suspendedAt ?? '' };
}

function buildNotificationDedupeKey(
  routingKey: string,
  messageId: string,
  userId: string,
  type: NotificationType,
): string {
  return `${routingKey}:${messageId}:${userId}:${type}`;
}
