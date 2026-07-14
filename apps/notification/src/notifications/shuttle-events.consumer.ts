import { Inject, Injectable, OnModuleInit } from '@nestjs/common';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import { z, ZodError } from 'zod';
import { NotificationType } from '../generated/notification-prisma-client';
import { RABBITMQ_PREFETCH_ONE } from './core-events.constants';
import { MessageIdempotencyService } from './message-idempotency.service';
import { NotificationsService } from './notifications.service';
import { createNotificationLogger } from './notification-logger';
import { OPERATOR_RECIPIENT_PROVIDER } from './parcel-subscription-operator-events.constants';
import type { OperatorRecipientProvider } from './operator-recipient.provider';

const AssignedSchema = z.object({
  shuttleTripId: z.string().uuid(),
  mainTripId: z.string().uuid(),
  bookingId: z.string().uuid(),
  passengerUserId: z.string().uuid(),
  ticketIds: z.array(z.string().uuid()).min(1),
  pickupOrder: z.number().int().positive(),
  scheduledDepartureTime: z.string(),
  scheduledEndTime: z.string(),
  driver: z.object({
    userId: z.string().uuid(),
    displayName: z.string().min(1),
    phone: z.string().min(1),
  }),
  vehicle: z.object({ id: z.string().uuid(), licensePlate: z.string().min(1) }),
});
const WarningSchema = z.object({
  mainTripId: z.string().uuid(),
  operatorId: z.string().uuid(),
  alertType: z.enum(['WARNING_120', 'WARNING_60']),
  pendingBookingCount: z.number().int().nonnegative(),
  pendingPassengerCount: z.number().int().nonnegative(),
  hardCutoffAt: z.string(),
});
const UnfulfilledSchema = z.object({
  mainTripId: z.string().uuid(),
  bookingId: z.string().uuid(),
  passengerUserId: z.string().uuid(),
  stationId: z.string().uuid(),
  reason: z.literal('AUTO_UNFULFILLED_CUTOFF'),
});

const bindings = [
  { queue: 'notification:shuttle-assigned', routingKey: 'trip.shuttle.assigned' },
  { queue: 'notification:shuttle-warning', routingKey: 'trip.shuttle.warning_issued' },
  { queue: 'notification:shuttle-unfulfilled', routingKey: 'trip.shuttle.unfulfilled' },
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
    const messageId = raw.properties.messageId ?? raw.properties.correlationId;
    if (!messageId) throw new Error(`MISSING_MESSAGE_ID_${routingKey}`);
    const state = await this.idempotency.begin(routingKey, messageId);
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
    if (routingKey === 'trip.shuttle.assigned') {
      const event = AssignedSchema.parse(payload);
      await this.notifications.createNotification({
        userId: event.passengerUserId,
        type: NotificationType.SHUTTLE_ASSIGNED,
        title: 'Đã xếp chuyến trung chuyển',
        body: `Xe ${event.vehicle.licensePlate}, tài xế ${event.driver.displayName}, thứ tự đón ${event.pickupOrder}.`,
        data: {
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
        },
        dedupeKey: `${routingKey}:${event.bookingId}`,
      });
      return 1;
    }
    if (routingKey === 'trip.shuttle.unfulfilled') {
      const event = UnfulfilledSchema.parse(payload);
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
    const event = WarningSchema.parse(payload);
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
