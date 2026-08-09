import { Inject, Injectable, OnModuleInit } from '@nestjs/common';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import {
  TRIP_SHUTTLE_ASSIGNED_ROUTING_KEY,
  TRIP_SHUTTLE_CANCELLED_ROUTING_KEY,
  TRIP_SHUTTLE_COMPLETED_ROUTING_KEY,
  TRIP_SHUTTLE_DELIVERED_ROUTING_KEY,
  TRIP_SHUTTLE_NO_SHOW_ROUTING_KEY,
  TRIP_SHUTTLE_PICKED_UP_ROUTING_KEY,
  TRIP_SHUTTLE_UNFULFILLED_ROUTING_KEY,
  TRIP_SHUTTLE_WARNING_ROUTING_KEY,
  TripShuttleAssignedEventSchema,
  TripShuttleLifecycleEventSchema,
  TripShuttleUnfulfilledEventSchema,
  TripShuttleWarningEventSchema,
} from '@vietride/contracts';
import type { ConsumeMessage } from 'amqplib';
import { z, ZodError } from 'zod';
import { NotificationType } from '../generated/notification-prisma-client';
import { RABBITMQ_PREFETCH_ONE } from './core-events.constants';
import { MessageIdempotencyService } from './message-idempotency.service';
import { NotificationsService } from './notifications.service';
import { createNotificationLogger } from './notification-logger';
import { OPERATOR_RECIPIENT_PROVIDER } from './parcel-subscription-operator-events.constants';
import type { OperatorRecipientProvider } from './operator-recipient.provider';

const bindings = [
  { queue: 'notification:shuttle-assigned', routingKey: TRIP_SHUTTLE_ASSIGNED_ROUTING_KEY },
  { queue: 'notification:shuttle-warning', routingKey: TRIP_SHUTTLE_WARNING_ROUTING_KEY },
  { queue: 'notification:shuttle-unfulfilled', routingKey: TRIP_SHUTTLE_UNFULFILLED_ROUTING_KEY },
  { queue: 'notification:shuttle-cancelled', routingKey: TRIP_SHUTTLE_CANCELLED_ROUTING_KEY },
  { queue: 'notification:shuttle-picked-up', routingKey: TRIP_SHUTTLE_PICKED_UP_ROUTING_KEY },
  { queue: 'notification:shuttle-delivered', routingKey: TRIP_SHUTTLE_DELIVERED_ROUTING_KEY },
  { queue: 'notification:shuttle-no-show', routingKey: TRIP_SHUTTLE_NO_SHOW_ROUTING_KEY },
  { queue: 'notification:shuttle-completed', routingKey: TRIP_SHUTTLE_COMPLETED_ROUTING_KEY },
] as const;

@Injectable()
export class ShuttleEventsConsumer implements OnModuleInit {
  private readonly logger = createNotificationLogger(ShuttleEventsConsumer.name);

  constructor(
    private readonly consumer: RabbitMqConsumer,
    private readonly idempotency: MessageIdempotencyService,
    private readonly notifications: NotificationsService,
    @Inject(OPERATOR_RECIPIENT_PROVIDER) private readonly recipients: OperatorRecipientProvider,
  ) {}

  async onModuleInit(): Promise<void> {
    await Promise.all(
      bindings.map((binding) =>
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

  async handle(routingKey: string, payload: unknown, raw: ConsumeMessage): Promise<void> {
    const payloadEventId = z.object({ eventId: z.string().uuid() }).safeParse(payload);
    const messageId = payloadEventId.success
      ? payloadEventId.data.eventId
      : raw.properties.messageId ?? raw.properties.correlationId;
    if (!messageId) throw new Error(`MISSING_MESSAGE_ID_${routingKey}`);
    const state = await this.idempotency.begin(routingKey, messageId, raw.content);
    if (state === 'duplicate') return;
    if (state === 'locked') throw new Error(`MESSAGE_LOCKED_${routingKey}_${messageId}`);
    try {
      const count = await this.createNotifications(routingKey, payload);
      await this.idempotency.markProcessed(routingKey, messageId);
      this.logger.info(
        { routingKey, messageId, notificationCount: count },
        'Processed shuttle notification event',
      );
    } catch (error) {
      if (error instanceof ZodError) {
        await this.idempotency.markProcessed(routingKey, messageId);
        this.logger.warn(
          { routingKey, messageId, issueCount: error.issues.length },
          'Dropped malformed shuttle event',
        );
        return;
      }
      await this.idempotency.release(routingKey, messageId);
      throw error;
    }
  }

  private async createNotifications(routingKey: string, payload: unknown): Promise<number> {
    if (routingKey === TRIP_SHUTTLE_ASSIGNED_ROUTING_KEY) {
      const event = TripShuttleAssignedEventSchema.parse(payload);
      const data = {
        shuttleTripId: event.shuttleTripId,
        mainTripId: event.mainTripId,
        bookingId: event.bookingId,
        ticketIds: event.ticketIds,
        pickupOrder: event.pickupOrder,
        scheduledDepartureTime: event.scheduledDepartureTime,
        scheduledEndTime: event.scheduledEndTime,
        driver: event.driver,
        vehicle: event.vehicle,
        deepLink: `vietride://tracking/shuttle/${event.shuttleTripId}`,
      };
      await Promise.all([
        this.notifications.createNotification({
          userId: event.passengerUserId,
          type: NotificationType.SHUTTLE_ASSIGNED,
          title: 'Đã xếp chuyến trung chuyển',
          body: `Xe ${event.vehicle.licensePlate}, tài xế ${event.driver.displayName}, thứ tự đón ${event.pickupOrder}.`,
          data,
          dedupeKey: `${routingKey}:${event.bookingId}:passenger:${event.passengerUserId}`,
        }),
        this.notifications.createNotification({
          userId: event.driver.userId,
          type: NotificationType.SHUTTLE_ASSIGNED,
          title: 'Bạn được phân công chuyến trung chuyển',
          body: `Chuyến trung chuyển có điểm đón thứ tự ${event.pickupOrder}.`,
          data,
          dedupeKey: `${routingKey}:${event.bookingId}:driver:${event.driver.userId}`,
        }),
      ]);
      return 2;
    }
    if (routingKey === TRIP_SHUTTLE_UNFULFILLED_ROUTING_KEY) {
      const event = TripShuttleUnfulfilledEventSchema.parse(payload);
      await this.notifications.createNotification({
        userId: event.passengerUserId,
        type: NotificationType.SHUTTLE_UNFULFILLED,
        title: 'Không thể bố trí xe trung chuyển',
        body: 'Vui lòng tự di chuyển đến bến khởi hành để kịp chuyến chính.',
        data: {
          mainTripId: event.mainTripId,
          bookingId: event.bookingId,
          stationId: event.stationId,
          reason: event.reason,
        },
        dedupeKey: `${routingKey}:${event.bookingId}`,
      });
      return 1;
    }
    if (routingKey.startsWith('trip.shuttle.') && routingKey !== TRIP_SHUTTLE_WARNING_ROUTING_KEY) {
      const event = TripShuttleLifecycleEventSchema.parse(payload);
      const eventId = event.eventId;
      const typeByRoutingKey = {
        [TRIP_SHUTTLE_CANCELLED_ROUTING_KEY]: NotificationType.SHUTTLE_CANCELLED,
        [TRIP_SHUTTLE_PICKED_UP_ROUTING_KEY]: NotificationType.SHUTTLE_PICKED_UP,
        [TRIP_SHUTTLE_DELIVERED_ROUTING_KEY]: NotificationType.SHUTTLE_DELIVERED,
        [TRIP_SHUTTLE_NO_SHOW_ROUTING_KEY]: NotificationType.SHUTTLE_NO_SHOW,
        [TRIP_SHUTTLE_COMPLETED_ROUTING_KEY]: NotificationType.SHUTTLE_COMPLETED,
      } as const;
      const type = typeByRoutingKey[routingKey as keyof typeof typeByRoutingKey];
      const data = { ...event, eventId: event.eventId };
      let count = 0;
      if (event.passengerUserId) {
        await this.notifications.createNotification({
          userId: event.passengerUserId,
          type,
          title: `Cập nhật trung chuyển: ${event.status}`,
          body: event.reason ?? `Trạng thái trung chuyển đã chuyển sang ${event.status}.`,
          data,
          dedupeKey: `${eventId}:passenger:${event.passengerUserId}`,
        });
        count++;
      }
      const operatorUserIds = [...new Set(await this.recipients.resolveOperatorRecipientUserIds(event.operatorId))];
      await Promise.all(operatorUserIds.map((userId) => this.notifications.createNotification({
        userId,
        type,
        title: `Trung chuyển: ${event.status}`,
        body: event.reason ?? 'Chuyến trung chuyển đã cập nhật trạng thái.',
        data,
        dedupeKey: `${eventId}:operator:${userId}`,
      })));
      return count + operatorUserIds.length;
    }
    const event = TripShuttleWarningEventSchema.parse(payload);
    const userIds = [
      ...new Set(await this.recipients.resolveOperatorRecipientUserIds(event.operatorId)),
    ];
    await Promise.all(
      userIds.map((userId) =>
        this.notifications.createNotification({
          userId,
          type: NotificationType.SHUTTLE_WARNING,
          title:
            event.alertType === 'WARNING_60'
              ? 'Khẩn: còn khách chờ trung chuyển'
              : 'Còn khách chờ trung chuyển',
          body: `${event.pendingPassengerCount} hành khách chưa được xếp xe.`,
          data: event,
          dedupeKey: `${routingKey}:${event.mainTripId}:${event.alertType}:${userId}`,
        }),
      ),
    );
    return userIds.length;
  }
}
