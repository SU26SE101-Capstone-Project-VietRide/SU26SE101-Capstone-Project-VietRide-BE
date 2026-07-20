import { Injectable, OnModuleInit } from '@nestjs/common';
import {
  TRIP_STOP_DEPARTED_WITH_PENDING_ROUTING_KEY,
  TripStopDepartedWithPendingEventSchema,
} from '@vietride/contracts';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import { RABBITMQ_PREFETCH_ONE } from './core-events.constants';
import { MessageIdempotencyService } from './message-idempotency.service';
import { createNotificationLogger } from './notification-logger';
import { NotificationsService } from './notifications.service';
import { mapTripTrackingAlertToNotifications } from './trip-tracking-alert-notification.mapper';

export const DAY24_DEPARTED_PENDING_QUEUE_BINDING = {
  queue: 'notification:trip-stop-departed-with-pending',
  routingKey: TRIP_STOP_DEPARTED_WITH_PENDING_ROUTING_KEY,
} as const;

@Injectable()
export class Day24DepartedPendingEventsConsumer implements OnModuleInit {
  private readonly logger = createNotificationLogger(Day24DepartedPendingEventsConsumer.name);

  constructor(
    private readonly consumer: RabbitMqConsumer,
    private readonly idempotency: MessageIdempotencyService,
    private readonly notificationsService: NotificationsService,
  ) {}

  async onModuleInit(): Promise<void> {
    await this.consumer.subscribe(
      DAY24_DEPARTED_PENDING_QUEUE_BINDING.queue,
      DAY24_DEPARTED_PENDING_QUEUE_BINDING.routingKey,
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
      throw new Error(`MISSING_MESSAGE_ID_${TRIP_STOP_DEPARTED_WITH_PENDING_ROUTING_KEY}`);
    }

    const parsed = TripStopDepartedWithPendingEventSchema.safeParse(payload);
    const eventId = parsed.success ? parsed.data.eventId : brokerMessageId;
    const processingState = await this.idempotency.begin(
      TRIP_STOP_DEPARTED_WITH_PENDING_ROUTING_KEY,
      eventId,
    );
    if (processingState === 'duplicate') return;
    if (processingState === 'locked') {
      throw new Error(`MESSAGE_LOCKED_${TRIP_STOP_DEPARTED_WITH_PENDING_ROUTING_KEY}_${eventId}`);
    }

    try {
      if (!parsed.success) {
        this.logger.warn(
          {
            routingKey: TRIP_STOP_DEPARTED_WITH_PENDING_ROUTING_KEY,
            eventId,
            issues: parsed.error.issues,
          },
          'Dropping malformed departed-with-pending event',
        );
        await this.idempotency.markProcessed(TRIP_STOP_DEPARTED_WITH_PENDING_ROUTING_KEY, eventId);
        return;
      }

      const notifications = mapTripTrackingAlertToNotifications(
        TRIP_STOP_DEPARTED_WITH_PENDING_ROUTING_KEY,
        parsed.data,
      );
      await Promise.all(
        notifications.map((notification) =>
          this.notificationsService.createNotification({
            ...notification,
            dedupeKey: `${TRIP_STOP_DEPARTED_WITH_PENDING_ROUTING_KEY}:${eventId}:${notification.userId}:${notification.type}`,
          }),
        ),
      );
      await this.idempotency.markProcessed(TRIP_STOP_DEPARTED_WITH_PENDING_ROUTING_KEY, eventId);
    } catch (error) {
      await this.idempotency.release(TRIP_STOP_DEPARTED_WITH_PENDING_ROUTING_KEY, eventId);
      throw error;
    }
  }
}

function getMessageId(raw: ConsumeMessage): string | undefined {
  const properties: unknown = raw.properties;
  if (typeof properties !== 'object' || properties === null) return undefined;

  const { messageId, correlationId } = properties as Record<string, unknown>;
  if (typeof messageId === 'string') return messageId;
  return typeof correlationId === 'string' ? correlationId : undefined;
}
