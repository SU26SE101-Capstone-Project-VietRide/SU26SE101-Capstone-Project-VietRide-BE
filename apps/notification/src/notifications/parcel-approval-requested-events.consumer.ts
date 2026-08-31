import { Injectable, OnModuleInit } from '@nestjs/common';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import {
  PARCEL_APPROVAL_REQUESTED_ROUTING_KEY,
  ParcelApprovalRequestedEventSchema,
} from '@vietride/contracts';
import { NotificationType } from '../generated/notification-prisma-client';
import { RABBITMQ_PREFETCH_ONE } from './core-events.constants';
import { MessageIdempotencyService } from './message-idempotency.service';
import { createNotificationLogger } from './notification-logger';
import { NotificationsService } from './notifications.service';

const QUEUE_NAME = 'notification:parcel-approval-requested';

@Injectable()
export class ParcelApprovalRequestedEventsConsumer implements OnModuleInit {
  private readonly logger = createNotificationLogger(ParcelApprovalRequestedEventsConsumer.name);

  constructor(
    private readonly consumer: RabbitMqConsumer,
    private readonly idempotency: MessageIdempotencyService,
    private readonly notifications: NotificationsService,
  ) {}

  async onModuleInit(): Promise<void> {
    await this.consumer.subscribe(
      QUEUE_NAME,
      PARCEL_APPROVAL_REQUESTED_ROUTING_KEY,
      (payload, raw) => this.handle(payload, raw),
      { prefetch: RABBITMQ_PREFETCH_ONE, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000 },
    );
  }

  async handle(payload: unknown, raw: ConsumeMessage): Promise<void> {
    const parsed = ParcelApprovalRequestedEventSchema.safeParse(payload);
    const messageId = parsed.success
      ? parsed.data.eventId
      : getMessageId(raw) ?? getPayloadEventId(payload);
    if (!messageId) throw new Error(`MISSING_MESSAGE_ID_${PARCEL_APPROVAL_REQUESTED_ROUTING_KEY}`);

    const state = await this.idempotency.begin(
      PARCEL_APPROVAL_REQUESTED_ROUTING_KEY,
      messageId,
      raw.content,
    );
    if (state === 'duplicate') return;
    if (state === 'locked') {
      throw new Error(`MESSAGE_LOCKED_${PARCEL_APPROVAL_REQUESTED_ROUTING_KEY}_${messageId}`);
    }

    try {
      if (!parsed.success) {
        this.logger.warn(
          { messageId, issues: parsed.error.issues },
          'Dropping malformed Parcel approval request event',
        );
        await this.idempotency.markProcessed(PARCEL_APPROVAL_REQUESTED_ROUTING_KEY, messageId);
        return;
      }

      const event = parsed.data;
      await this.notifications.createNotification({
        userId: event.targetDriverUserId,
        type: NotificationType.PARCEL_APPROVAL_REQUESTED,
        title: 'Có yêu cầu đơn gửi hàng cần phê duyệt',
        body: event.requestType === 'STOP_DEPARTURE'
          ? 'Phụ xe yêu cầu phê duyệt rời điểm dừng khi còn đơn gửi hàng chưa đối soát.'
          : 'Phụ xe yêu cầu phê duyệt ngoại lệ bàn giao đơn gửi hàng.',
        data: {
          eventId: event.eventId,
          occurredAt: event.occurredAt,
          requestId: event.approvalRequestId,
          requestType: event.requestType,
          operatorId: event.operatorId,
          tripId: event.tripId,
          parcelId: event.parcelId,
          incidentId: event.incidentId,
          stopId: event.stopId,
          expiresAt: event.expiresAt,
          validityCondition: event.validityCondition,
        },
        dedupeKey: `${PARCEL_APPROVAL_REQUESTED_ROUTING_KEY}:${event.eventId}:${event.targetDriverUserId}:${NotificationType.PARCEL_APPROVAL_REQUESTED}`,
      });

      await this.idempotency.markProcessed(PARCEL_APPROVAL_REQUESTED_ROUTING_KEY, messageId);
      this.logger.info(
        { messageId, requestId: event.approvalRequestId, targetDriverUserId: event.targetDriverUserId },
        'Processed Parcel approval request notification',
      );
    } catch (error) {
      await this.idempotency.release(PARCEL_APPROVAL_REQUESTED_ROUTING_KEY, messageId);
      throw error;
    }
  }
}

function getMessageId(raw: ConsumeMessage): string | undefined {
  const { messageId, correlationId } = raw.properties;
  if (typeof messageId === 'string' && messageId.length > 0) return messageId;
  return typeof correlationId === 'string' && correlationId.length > 0 ? correlationId : undefined;
}

function getPayloadEventId(payload: unknown): string | undefined {
  if (typeof payload !== 'object' || payload === null || !('eventId' in payload)) return undefined;
  const eventId = (payload as { eventId?: unknown }).eventId;
  return typeof eventId === 'string' && eventId.length > 0 ? eventId : undefined;
}
