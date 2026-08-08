import { Injectable, OnModuleInit } from '@nestjs/common';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import {
  BOOKING_CANCELLED_ROUTING_KEY,
  OperationalBookingCancelledEventSchema,
} from '@vietride/contracts';
import { NotificationType } from '../generated/notification-prisma-client';
import { RABBITMQ_PREFETCH_ONE } from './core-events.constants';
import { MessageIdempotencyService } from './message-idempotency.service';
import { NotificationsService } from './notifications.service';
import { TripAnnouncementRecipientProvider } from './trip-announcement-recipient.provider';
import { createNotificationLogger } from './notification-logger';

const QUEUE_NAME = 'notification:booking-cancelled-crew';
const TERMINAL_TRIP_REASONS = new Set(['OPERATOR_CANCELLED_TRIP', 'TRIP_DISRUPTED']);

@Injectable()
export class BookingCancelledCrewEventsConsumer implements OnModuleInit {
  private readonly logger = createNotificationLogger(BookingCancelledCrewEventsConsumer.name);

  constructor(
    private readonly consumer: RabbitMqConsumer,
    private readonly idempotency: MessageIdempotencyService,
    private readonly notifications: NotificationsService,
    private readonly tripRecipients: TripAnnouncementRecipientProvider,
  ) {}

  async onModuleInit(): Promise<void> {
    await this.consumer.subscribe(
      QUEUE_NAME,
      BOOKING_CANCELLED_ROUTING_KEY,
      (payload, raw) => this.handle(payload, raw),
      { prefetch: RABBITMQ_PREFETCH_ONE, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000 },
    );
  }

  async handle(payload: unknown, raw: ConsumeMessage): Promise<void> {
    const parsed = OperationalBookingCancelledEventSchema.safeParse(payload);
    const messageId = parsed.success
      ? parsed.data.eventId
      : getMessageId(raw) ?? getPayloadEventId(payload);
    if (!messageId) throw new Error(`MISSING_MESSAGE_ID_${BOOKING_CANCELLED_ROUTING_KEY}`);

    const state = await this.idempotency.begin(
      `${BOOKING_CANCELLED_ROUTING_KEY}:crew`,
      messageId,
      raw.content,
    );
    if (state === 'duplicate') return;
    if (state === 'locked') throw new Error(`MESSAGE_LOCKED_${BOOKING_CANCELLED_ROUTING_KEY}_${messageId}`);

    try {
      if (
        !parsed.success ||
        parsed.data.previousStatus !== 'CONFIRMED' ||
        TERMINAL_TRIP_REASONS.has(parsed.data.cancellationReason)
      ) {
        await this.idempotency.markProcessed(`${BOOKING_CANCELLED_ROUTING_KEY}:crew`, messageId);
        return;
      }

      const event = parsed.data;
      const trip = await this.tripRecipients.getTripRecipientSnapshot(event.tripId);
      if (!trip.departureDateTime) throw new Error(`TRIP_DEPARTURE_REQUIRED_${event.tripId}`);
      const recipientUserIds = [...new Set(trip.crewUserIds)];

      await Promise.all(recipientUserIds.map((userId) => this.notifications.createNotification({
        userId,
        type: NotificationType.BOOKING_CANCELLED,
        title: 'Booking trên chuyến đã bị hủy',
        body: `Booking ${event.bookingCode} đã bị hủy và được gỡ khỏi danh sách đón khách.`,
        data: {
          eventId: event.eventId,
          occurredAt: event.occurredAt,
          bookingId: event.bookingId,
          bookingCode: event.bookingCode,
          tripId: event.tripId,
          seatNumbers: event.seatNumbers,
          departureDateTime: trip.departureDateTime,
          cancellationReason: event.cancellationReason,
          deepLink: `vietride://driver/trips/${event.tripId}/bookings/${event.bookingId}`,
        },
        dedupeKey: `${BOOKING_CANCELLED_ROUTING_KEY}:${event.eventId}:${userId}:crew`,
      })));

      await this.idempotency.markProcessed(`${BOOKING_CANCELLED_ROUTING_KEY}:crew`, messageId);
      this.logger.info(
        { messageId, bookingId: event.bookingId, recipientCount: recipientUserIds.length },
        'Processed booking-cancelled crew notification',
      );
    } catch (error) {
      await this.idempotency.release(`${BOOKING_CANCELLED_ROUTING_KEY}:crew`, messageId);
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
