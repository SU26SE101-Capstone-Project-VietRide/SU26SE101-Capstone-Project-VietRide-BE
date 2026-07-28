import { Injectable, OnModuleInit } from '@nestjs/common';
import {
  BOOKING_PASSENGER_NO_SHOW_MARKED_ROUTING_KEY,
  BookingPassengerNoShowMarkedEventSchema,
  type BookingPassengerNoShowMarkedEvent,
} from '@vietride/contracts';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import { NotificationType } from '../generated/notification-prisma-client';
import { RABBITMQ_PREFETCH_ONE } from './core-events.constants';
import type { CreateNotificationDto } from './dto/create-notification.dto';
import { MessageIdempotencyService } from './message-idempotency.service';
import { createNotificationLogger } from './notification-logger';
import { NotificationsService } from './notifications.service';

export const DAY24_NO_SHOW_QUEUE_BINDING = {
  queue: 'notification:booking-passenger-no-show-marked',
  routingKey: BOOKING_PASSENGER_NO_SHOW_MARKED_ROUTING_KEY,
} as const;

@Injectable()
export class Day24NoShowEventsConsumer implements OnModuleInit {
  private readonly logger = createNotificationLogger(Day24NoShowEventsConsumer.name);

  constructor(
    private readonly consumer: RabbitMqConsumer,
    private readonly idempotency: MessageIdempotencyService,
    private readonly notificationsService: NotificationsService,
  ) {}

  async onModuleInit(): Promise<void> {
    await this.consumer.subscribe(
      DAY24_NO_SHOW_QUEUE_BINDING.queue,
      DAY24_NO_SHOW_QUEUE_BINDING.routingKey,
      (payload, raw) => this.handle(payload, raw),
      {
        prefetch: RABBITMQ_PREFETCH_ONE,
        deadLetter: true,
        maxRetries: 5,
        retryDelayMs: 10_000,
      },
    );
  }

  async handle(payload: unknown, raw: ConsumeMessage): Promise<void> {
    const brokerMessageId = getMessageId(raw);
    if (!brokerMessageId) {
      throw new Error(`MISSING_MESSAGE_ID_${BOOKING_PASSENGER_NO_SHOW_MARKED_ROUTING_KEY}`);
    }

    const parsed = BookingPassengerNoShowMarkedEventSchema.safeParse(payload);
    const eventId = parsed.success ? parsed.data.eventId : brokerMessageId;
    const processingState = await this.idempotency.begin(
      BOOKING_PASSENGER_NO_SHOW_MARKED_ROUTING_KEY,
      eventId,
      raw.content,
    );
    if (processingState === 'duplicate') return;
    if (processingState === 'locked') {
      throw new Error(`MESSAGE_LOCKED_${BOOKING_PASSENGER_NO_SHOW_MARKED_ROUTING_KEY}_${eventId}`);
    }

    try {
      if (!parsed.success) {
        this.logger.warn(
          {
            routingKey: BOOKING_PASSENGER_NO_SHOW_MARKED_ROUTING_KEY,
            eventId,
            issues: parsed.error.issues,
          },
          'Dropping malformed passenger no-show event',
        );
        await this.idempotency.markProcessed(BOOKING_PASSENGER_NO_SHOW_MARKED_ROUTING_KEY, eventId);
        return;
      }

      const notification = mapPassengerNoShowToNotification(parsed.data);
      await this.notificationsService.createNotification({
        ...notification,
        dedupeKey: `${BOOKING_PASSENGER_NO_SHOW_MARKED_ROUTING_KEY}:${eventId}:${notification.userId}:${notification.type}`,
      });
      await this.idempotency.markProcessed(BOOKING_PASSENGER_NO_SHOW_MARKED_ROUTING_KEY, eventId);
    } catch (error) {
      await this.idempotency.release(BOOKING_PASSENGER_NO_SHOW_MARKED_ROUTING_KEY, eventId);
      throw error;
    }
  }
}

export function mapPassengerNoShowToNotification(payload: unknown): CreateNotificationDto {
  return mapParsedPassengerNoShow(BookingPassengerNoShowMarkedEventSchema.parse(payload));
}

function mapParsedPassengerNoShow(
  payload: BookingPassengerNoShowMarkedEvent,
): CreateNotificationDto {
  return {
    userId: payload.userId,
    type: NotificationType.PASSENGER_NO_SHOW,
    title: 'Bạn đã lỡ chuyến xe',
    body: 'Bạn đã không lên xe đúng giờ. Vé không được hoàn tiền theo chính sách.',
    data: {
      eventId: payload.eventId,
      occurredAt: payload.occurredAt,
      eventType: payload.eventType,
      bookingId: payload.bookingId,
      tripId: payload.tripId,
      bookingStatus: payload.bookingStatus,
      newlyNoShowPassengerIds: payload.newlyNoShowPassengerIds,
      triggerType: payload.triggerType,
      pickupStopId: payload.pickupStopId ?? null,
    },
  };
}

function getMessageId(raw: ConsumeMessage): string | undefined {
  const properties: unknown = raw.properties;
  if (typeof properties !== 'object' || properties === null) return undefined;

  const { messageId, correlationId } = properties as Record<string, unknown>;
  if (typeof messageId === 'string') return messageId;
  return typeof correlationId === 'string' ? correlationId : undefined;
}
