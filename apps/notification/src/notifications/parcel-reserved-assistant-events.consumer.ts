import { Injectable, OnModuleInit } from '@nestjs/common';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import {
  PARCEL_RESERVED_ROUTING_KEY,
  ParcelReservedEventSchema,
} from '@vietride/contracts';
import { NotificationType } from '../generated/notification-prisma-client';
import { RABBITMQ_PREFETCH_ONE } from './core-events.constants';
import { MessageIdempotencyService } from './message-idempotency.service';
import { createNotificationLogger } from './notification-logger';
import { NotificationsService } from './notifications.service';
import { TripAnnouncementRecipientProvider } from './trip-announcement-recipient.provider';

const QUEUE_NAME = 'notification:parcel-reserved-assistant';

@Injectable()
export class ParcelReservedAssistantEventsConsumer implements OnModuleInit {
  private readonly logger = createNotificationLogger(ParcelReservedAssistantEventsConsumer.name);

  constructor(
    private readonly consumer: RabbitMqConsumer,
    private readonly idempotency: MessageIdempotencyService,
    private readonly notifications: NotificationsService,
    private readonly tripRecipients: TripAnnouncementRecipientProvider,
  ) {}

  async onModuleInit(): Promise<void> {
    await this.consumer.subscribe(
      QUEUE_NAME,
      PARCEL_RESERVED_ROUTING_KEY,
      (payload, raw) => this.handle(payload, raw),
      { prefetch: RABBITMQ_PREFETCH_ONE, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000 },
    );
  }

  async handle(payload: unknown, raw: ConsumeMessage): Promise<void> {
    const parsed = ParcelReservedEventSchema.safeParse(payload);
    const messageId = parsed.success
      ? parsed.data.eventId
      : getMessageId(raw) ?? getPayloadEventId(payload);
    if (!messageId) throw new Error(`MISSING_MESSAGE_ID_${PARCEL_RESERVED_ROUTING_KEY}`);

    const state = await this.idempotency.begin(
      PARCEL_RESERVED_ROUTING_KEY,
      messageId,
      raw.content,
    );
    if (state === 'duplicate') return;
    if (state === 'locked') {
      throw new Error(`MESSAGE_LOCKED_${PARCEL_RESERVED_ROUTING_KEY}_${messageId}`);
    }

    try {
      if (!parsed.success) {
        this.logger.warn(
          { messageId, issues: parsed.error.issues },
          'Dropping malformed parcel reserved event',
        );
        await this.idempotency.markProcessed(PARCEL_RESERVED_ROUTING_KEY, messageId);
        return;
      }

      const event = parsed.data;
      const assistantUserId = await this.tripRecipients.resolveTripAssistantUserId(
        event.tripId,
        event.operatorId,
      );
      if (assistantUserId) {
        await this.notifications.createNotification({
          userId: assistantUserId,
          type: NotificationType.PARCEL_RESERVED,
          title: 'Có đơn hàng mới cần check-in',
          body: `Đơn hàng ${event.parcelCode} đã thanh toán cọc và được giữ chỗ trên chuyến.`,
          data: {
            eventId: event.eventId,
            occurredAt: event.occurredAt,
            parcelId: event.parcelId,
            parcelCode: event.parcelCode,
            tripId: event.tripId,
          },
          dedupeKey: `${PARCEL_RESERVED_ROUTING_KEY}:${event.eventId}:${assistantUserId}:${NotificationType.PARCEL_RESERVED}`,
        });
      }

      await this.idempotency.markProcessed(PARCEL_RESERVED_ROUTING_KEY, messageId);
      this.logger.info(
        { messageId, parcelId: event.parcelId, recipientCount: assistantUserId ? 1 : 0 },
        'Processed parcel reserved Assistant notification',
      );
    } catch (error) {
      await this.idempotency.release(PARCEL_RESERVED_ROUTING_KEY, messageId);
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
