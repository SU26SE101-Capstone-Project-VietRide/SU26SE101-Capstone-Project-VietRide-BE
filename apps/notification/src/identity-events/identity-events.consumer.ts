import { Inject, Injectable, Logger, OnModuleInit } from '@nestjs/common';
import {
  IDENTITY_OPERATOR_APPROVED_ROUTING_KEY,
  IDENTITY_OPERATOR_REGISTRATION_SUBMITTED_ROUTING_KEY,
  IDENTITY_OPERATOR_SUSPENDED_ROUTING_KEY,
  IDENTITY_OTP_REQUESTED_ROUTING_KEY,
  IDENTITY_SUBSCRIPTION_USAGE_WARNING_ROUTING_KEY,
  IDENTITY_USER_CREATED_ROUTING_KEY,
  IdentityOperatorApprovedEventSchema,
  IdentityOperatorRegistrationSubmittedEventSchema,
  IdentityOperatorSuspendedEventSchema,
  IdentityOtpRequestedEventSchema,
  IdentitySubscriptionUsageWarningEventSchema,
  IdentityUserCreatedEventSchema,
} from '@vietride/contracts';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import { ZodError } from 'zod';
import { EmailTemplateKey, NotificationType } from '../generated/notification-prisma-client';
import { RABBITMQ_PREFETCH_ONE } from '../notifications/core-events.constants';
import { formatSubscriptionPeriod } from '../notifications/notification-display';
import { MessageIdempotencyService } from '../notifications/message-idempotency.service';
import { NotificationsService } from '../notifications/notifications.service';
import { IdentitySystemAdminRecipientProvider } from '../notifications/identity-system-admin-recipient.provider';
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
    private readonly systemAdminRecipientProvider: IdentitySystemAdminRecipientProvider,
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
      'notification.identity.operator.registration-submitted',
      IDENTITY_OPERATOR_REGISTRATION_SUBMITTED_ROUTING_KEY,
      (payload, raw) => this.handleOperatorRegistrationSubmitted(payload, raw),
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
      'notification.identity.subscription.usage-warning',
      IDENTITY_SUBSCRIPTION_USAGE_WARNING_ROUTING_KEY,
      (payload, raw) => this.handleSubscriptionUsageWarning(payload, raw),
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
      'Nhà xe đã được duyệt',
      'Nhà xe của bạn đã được duyệt. Bạn có thể đăng nhập và bắt đầu vận hành.',
    );
  }

  async handleOperatorRegistrationSubmitted(
    payload: unknown,
    raw: ConsumeMessage,
  ): Promise<void> {
    const routingKey = IDENTITY_OPERATOR_REGISTRATION_SUBMITTED_ROUTING_KEY;
    const messageId = getMessageId(raw);
    if (!messageId) throw new Error(`MISSING_MESSAGE_ID_${routingKey}`);

    const processingState = await this.idempotency.begin(routingKey, messageId, raw.content);
    if (processingState === 'duplicate') return;
    if (processingState === 'locked') throw new Error(`MESSAGE_LOCKED_${routingKey}_${messageId}`);

    try {
      const event = IdentityOperatorRegistrationSubmittedEventSchema.parse(payload);
      const recipientUserIds =
        await this.systemAdminRecipientProvider.resolveSystemAdminRecipientUserIds();
      await Promise.all(
        recipientUserIds.map((userId) =>
          this.notificationsService.createNotification({
            userId,
            type: NotificationType.OPERATOR_REGISTRATION_SUBMITTED,
            title: 'Đơn đăng ký nhà xe mới',
            body: `Nhà xe ${event.companyName} vừa gửi hồ sơ đăng ký và đang chờ xét duyệt.`,
            data: {
              eventId: event.eventId,
              occurredAt: event.occurredAt,
              operatorId: event.operatorId,
              companyName: event.companyName,
            },
            dedupeKey: buildNotificationDedupeKey(
              routingKey,
              messageId,
              userId,
              NotificationType.OPERATOR_REGISTRATION_SUBMITTED,
            ),
          }),
        ),
      );
      await this.idempotency.markProcessed(routingKey, messageId);
    } catch (error) {
      if (error instanceof ZodError) {
        await this.idempotency.markProcessed(routingKey, messageId);
        return;
      }
      await this.idempotency.release(routingKey, messageId);
      throw error;
    }
  }

  async handleOperatorSuspended(payload: unknown, raw: ConsumeMessage): Promise<void> {
    await this.handleOperatorLifecycle(
      IDENTITY_OPERATOR_SUSPENDED_ROUTING_KEY,
      payload,
      raw,
      NotificationType.OPERATOR_SUSPENDED,
      'Nhà xe bị tạm ngưng',
      'Nhà xe của bạn đã bị tạm ngưng. Vui lòng liên hệ quản trị hệ thống để được hỗ trợ.',
    );
  }

  async handleSubscriptionUsageWarning(payload: unknown, raw: ConsumeMessage): Promise<void> {
    const routingKey = IDENTITY_SUBSCRIPTION_USAGE_WARNING_ROUTING_KEY;
    const messageId = getMessageId(raw);
    if (!messageId) throw new Error(`MISSING_MESSAGE_ID_${routingKey}`);

    const processingState = await this.idempotency.begin(routingKey, messageId, raw.content);
    if (processingState === 'duplicate') return;
    if (processingState === 'locked') throw new Error(`MESSAGE_LOCKED_${routingKey}_${messageId}`);

    try {
      const event = IdentitySubscriptionUsageWarningEventSchema.parse(payload);
      const recipientUserIds =
        await this.operatorRecipientProvider.resolveOperatorRecipientUserIds(event.operatorId);
      if (recipientUserIds.length === 0) {
        this.logger.warn(
          `No active operator admin recipients for ${routingKey} operatorId=${event.operatorId} messageId=${messageId}`,
        );
        await this.idempotency.markProcessed(routingKey, messageId);
        return;
      }

      await Promise.all(
        [...new Set(recipientUserIds)].map((userId) =>
          this.notificationsService.createNotification({
            userId,
            type: NotificationType.SUBSCRIPTION_USAGE_WARNING,
            title: 'Sắp đạt giới hạn gói dịch vụ',
            body: `Nhà xe đã sử dụng ${event.used}/${event.limit} hạn mức ${formatSubscriptionResource(event.resource)}${formatSubscriptionPeriod(event.periodKey)} (${event.usagePercent}%).`,
            data: event,
            dedupeKey: buildNotificationDedupeKey(
              routingKey,
              messageId,
              userId,
              NotificationType.SUBSCRIPTION_USAGE_WARNING,
            ),
          }),
        ),
      );
      await this.idempotency.markProcessed(routingKey, messageId);
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

  async handleOtpRequested(payload: unknown, raw: ConsumeMessage): Promise<void> {
    const routingKey = IDENTITY_OTP_REQUESTED_ROUTING_KEY;
    const messageId = getMessageId(raw);
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
        dedupeKey: `${routingKey}:${messageId}:email`,
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
    const messageId = getMessageId(raw);
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

      const createdNotifications = await Promise.all(
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
      if (routingKey === IDENTITY_OPERATOR_APPROVED_ROUTING_KEY) {
        await this.enqueueOperatorApprovedEmails(
          routingKey,
          messageId,
          event.operatorId,
          recipientUserIds,
          createdNotifications,
        );
      }
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

  private async enqueueOperatorApprovedEmails(
    routingKey: typeof IDENTITY_OPERATOR_APPROVED_ROUTING_KEY,
    messageId: string,
    operatorId: string,
    recipientUserIds: string[],
    notifications: Awaited<ReturnType<NotificationsService['createNotification']>>[],
  ): Promise<void> {
    const resolveEmails = this.operatorRecipientProvider.resolveOperatorRecipientEmails;
    if (!resolveEmails) {
      throw new Error('OPERATOR_RECIPIENT_EMAIL_PROVIDER_NOT_CONFIGURED');
    }

    const recipientEmails = await resolveEmails.call(
      this.operatorRecipientProvider,
      operatorId,
      recipientUserIds,
    );
    const notificationByUserId = new Map(
      notifications.map((notification) => [notification.userId, notification]),
    );

    await Promise.all(
      recipientEmails.map((recipient) => {
        const notification = notificationByUserId.get(recipient.userId);
        if (!notification) return Promise.resolve();
        return this.notificationsService.enqueueEmail({
          notificationId: notification.id,
          dedupeKey: `${routingKey}:${messageId}:${recipient.userId}:email`,
          toEmail: recipient.email,
          templateKey: EmailTemplateKey.OPERATOR_SUBSCRIPTION_NOTICE,
          templateData: {
            title: 'Nhà xe đã được duyệt',
            message: 'Nhà xe của bạn đã được duyệt. Bạn có thể đăng nhập và bắt đầu vận hành.',
          },
        });
      }),
    );
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

function formatSubscriptionResource(resource: string): string {
  switch (resource) {
    case 'VEHICLES':
      return 'phương tiện';
    case 'DRIVERS':
      return 'tài xế';
    case 'ASSISTANTS':
      return 'phụ xe';
    case 'OPERATOR_USERS':
      return 'người dùng nhà xe';
    case 'ROUTES':
      return 'tuyến đường';
    case 'TRIPS_THIS_MONTH':
      return 'chuyến xe trong tháng';
    default:
      return resource;
  }
}

function buildNotificationDedupeKey(
  routingKey: string,
  messageId: string,
  userId: string,
  type: NotificationType,
): string {
  return `${routingKey}:${messageId}:${userId}:${type}`;
}

function getMessageId(raw: ConsumeMessage): string | undefined {
  const properties: unknown = raw.properties;
  if (typeof properties !== 'object' || properties === null) return undefined;
  const { messageId, correlationId } = properties as Record<string, unknown>;
  if (typeof messageId === 'string') return messageId;
  return typeof correlationId === 'string' ? correlationId : undefined;
}
