import { Inject, Injectable, OnModuleInit } from '@nestjs/common';
import { TripCargoThresholdCrossedEventSchema } from '@vietride/contracts';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import { ZodError } from 'zod';
import { RABBITMQ_PREFETCH_ONE } from './core-events.constants';
import { MessageIdempotencyService } from './message-idempotency.service';
import { NotificationsService } from './notifications.service';
import { BookingTripRecipientProvider } from './booking-trip-recipient.provider';
import { TripAnnouncementRecipientProvider } from './trip-announcement-recipient.provider';
import { createNotificationLogger } from './notification-logger';
import type { OperatorRecipientProvider } from './operator-recipient.provider';
import { OPERATOR_RECIPIENT_PROVIDER } from './parcel-subscription-operator-events.constants';
import {
  TRIP_CARGO_THRESHOLD_CROSSED_ROUTING_KEY,
  TRIP_BOARDING_STARTED_ROUTING_KEY,
  TRIP_INCIDENT_REPORTED_ROUTING_KEY,
  TRIP_DELAYED_ROUTING_KEY,
  TRIP_ROUTE_CHANGED_ROUTING_KEY,
  TRIP_SCHEDULE_CHANGED_ROUTING_KEY,
  TRACKING_GPS_OFF_ROUTE_ROUTING_KEY,
  TRIP_TRACKING_ALERT_QUEUE_BINDINGS,
} from './trip-tracking-alert-events.constants';
import {
  IncidentReportedPayloadSchema,
  mapIncidentReportedToNotifications,
  mapTripCargoThresholdCrossedToNotifications,
  mapTripTrackingAlertToNotifications,
  parseTripRecipientResolutionRequest,
  type TripTrackingAlertRoutingKey,
} from './trip-tracking-alert-notification.mapper';

@Injectable()
export class TripTrackingAlertEventsConsumer implements OnModuleInit {
  private readonly logger = createNotificationLogger(TripTrackingAlertEventsConsumer.name);

  constructor(
    private readonly consumer: RabbitMqConsumer,
    private readonly idempotency: MessageIdempotencyService,
    private readonly notificationsService: NotificationsService,
    @Inject(OPERATOR_RECIPIENT_PROVIDER)
    private readonly operatorRecipients: OperatorRecipientProvider,
    private readonly bookingTripRecipients: BookingTripRecipientProvider,
    private readonly tripRecipients: TripAnnouncementRecipientProvider,
  ) {}

  async onModuleInit(): Promise<void> {
    await Promise.all(
      TRIP_TRACKING_ALERT_QUEUE_BINDINGS.map((binding) =>
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
    routingKey: TripTrackingAlertRoutingKey,
    payload: unknown,
    raw: ConsumeMessage,
  ): Promise<void> {
    if (routingKey === TRIP_CARGO_THRESHOLD_CROSSED_ROUTING_KEY) {
      await this.handleCargoThresholdCrossed(payload, raw);
      return;
    }
    if (routingKey === TRIP_INCIDENT_REPORTED_ROUTING_KEY) {
      await this.handleIncidentReported(payload, raw);
      return;
    }

    const messageId = getMessageId(raw);
    if (!messageId) {
      throw new Error(`MISSING_MESSAGE_ID_${routingKey}`);
    }

    const processingState = await this.idempotency.begin(routingKey, messageId, raw.content);
    if (processingState === 'duplicate') {
      this.logger.info(
        { routingKey, messageId, processingState },
        'Skipping already handled alert message',
      );
      return;
    }
    if (processingState === 'locked') {
      throw new Error(`MESSAGE_LOCKED_${routingKey}_${messageId}`);
    }

    try {
      const resolvedRecipientUserIds = await this.resolveRecipientUserIds(routingKey, payload);
      const notifications = mapTripTrackingAlertToNotifications(
        routingKey,
        payload,
        resolvedRecipientUserIds,
      );
      await Promise.all(
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
      await this.idempotency.markProcessed(routingKey, messageId);
      this.logger.info(
        { routingKey, messageId, notificationCount: notifications.length },
        'Processed trip/tracking alert notification event',
      );
    } catch (error) {
      if (error instanceof ZodError) {
        this.logger.warn(
          { routingKey, messageId, issues: error.issues },
          'Dropping malformed alert notification event',
        );
        await this.idempotency.markProcessed(routingKey, messageId);
        return;
      }

      await this.idempotency.release(routingKey, messageId);
      throw error;
    }
  }

  private async resolveRecipientUserIds(
    routingKey: TripTrackingAlertRoutingKey,
    payload: unknown,
  ): Promise<string[] | undefined> {
    const request = parseTripRecipientResolutionRequest(routingKey, payload);
    if (!request) return undefined;

    if (routingKey === TRIP_BOARDING_STARTED_ROUTING_KEY) {
      return this.bookingTripRecipients.resolveTripPassengerUserIds(request.tripId);
    }

    if (routingKey === TRIP_DELAYED_ROUTING_KEY) {
      const snapshot = await this.tripRecipients.getTripRecipientSnapshot(request.tripId);
      const [passengers, admins] = await Promise.all([
        this.bookingTripRecipients.resolveTripPassengerUserIds(request.tripId),
        this.operatorRecipients.resolveOperatorRecipientUserIds(snapshot.operatorId),
      ]);
      return [...new Set([...passengers, ...admins])];
    }

    if (routingKey === TRACKING_GPS_OFF_ROUTE_ROUTING_KEY) {
      const snapshot = await this.tripRecipients.getTripRecipientSnapshot(request.tripId);
      const admins = await this.operatorRecipients.resolveOperatorRecipientUserIds(
        snapshot.operatorId,
      );
      return [...new Set([...snapshot.crewUserIds, ...admins])];
    }

    if (!request.operatorId) throw new Error(`MISSING_OPERATOR_ID_${routingKey}`);
    const crew = await this.tripRecipients.resolveTripCrewUserIds(
      request.tripId,
      request.operatorId,
    );
    if (routingKey === TRIP_SCHEDULE_CHANGED_ROUTING_KEY) return [...new Set(crew)];

    if (routingKey === TRIP_ROUTE_CHANGED_ROUTING_KEY) {
      const passengers = await this.bookingTripRecipients.resolveAffectedTripPassengerUserIds(
        request.tripId,
        request.affectedBookingIds,
      );
      return [...new Set([...crew, ...passengers])];
    }

    return undefined;
  }

  private async handleCargoThresholdCrossed(payload: unknown, raw: ConsumeMessage): Promise<void> {
    const parsed = TripCargoThresholdCrossedEventSchema.safeParse(payload);
    const brokerMessageId = getMessageId(raw);
    const messageId = parsed.success ? parsed.data.eventId : brokerMessageId;
    if (!messageId) {
      throw new Error(`MISSING_MESSAGE_ID_${TRIP_CARGO_THRESHOLD_CROSSED_ROUTING_KEY}`);
    }

    const processingState = await this.idempotency.begin(
      TRIP_CARGO_THRESHOLD_CROSSED_ROUTING_KEY,
      messageId,
      raw.content,
    );
    if (processingState === 'duplicate') return;
    if (processingState === 'locked') {
      throw new Error(`MESSAGE_LOCKED_${TRIP_CARGO_THRESHOLD_CROSSED_ROUTING_KEY}_${messageId}`);
    }

    try {
      if (!parsed.success) {
        await this.idempotency.markProcessed(TRIP_CARGO_THRESHOLD_CROSSED_ROUTING_KEY, messageId);
        return;
      }
      const recipientUserIds = [
        ...new Set(
          await this.operatorRecipients.resolveOperatorRecipientUserIds(parsed.data.operatorId),
        ),
      ];
      const notifications = mapTripCargoThresholdCrossedToNotifications(
        parsed.data,
        recipientUserIds,
      );
      await Promise.all(
        notifications.map((notification) =>
          this.notificationsService.createNotification({
            ...notification,
            dedupeKey: buildNotificationDedupeKey(
              TRIP_CARGO_THRESHOLD_CROSSED_ROUTING_KEY,
              messageId,
              notification.userId,
              notification.type,
            ),
          }),
        ),
      );
      await this.idempotency.markProcessed(TRIP_CARGO_THRESHOLD_CROSSED_ROUTING_KEY, messageId);
    } catch (error) {
      await this.idempotency.release(TRIP_CARGO_THRESHOLD_CROSSED_ROUTING_KEY, messageId);
      throw error;
    }
  }

  private async handleIncidentReported(payload: unknown, raw: ConsumeMessage): Promise<void> {
    const parsed = IncidentReportedPayloadSchema.safeParse(payload);
    const brokerMessageId = getMessageId(raw);
    const messageId = parsed.success ? (parsed.data.eventId ?? brokerMessageId) : brokerMessageId;
    if (!messageId) {
      throw new Error(`MISSING_MESSAGE_ID_${TRIP_INCIDENT_REPORTED_ROUTING_KEY}`);
    }

    const processingState = await this.idempotency.begin(
      TRIP_INCIDENT_REPORTED_ROUTING_KEY,
      messageId,
      raw.content,
    );
    if (processingState === 'duplicate') {
      this.logger.info(
        { routingKey: TRIP_INCIDENT_REPORTED_ROUTING_KEY, messageId, processingState },
        'Skipping already handled incident notification message',
      );
      return;
    }
    if (processingState === 'locked') {
      throw new Error(`MESSAGE_LOCKED_${TRIP_INCIDENT_REPORTED_ROUTING_KEY}_${messageId}`);
    }

    try {
      if (!parsed.success) {
        this.logger.warn(
          {
            routingKey: TRIP_INCIDENT_REPORTED_ROUTING_KEY,
            messageId,
            issues: parsed.error.issues.map((issue) => ({ code: issue.code, path: issue.path })),
          },
          'Dropping malformed incident notification event',
        );
        await this.idempotency.markProcessed(TRIP_INCIDENT_REPORTED_ROUTING_KEY, messageId);
        return;
      }

      const recipientUserIds = [
        ...new Set(
          await this.operatorRecipients.resolveOperatorRecipientUserIds(parsed.data.operatorId),
        ),
      ];
      const notifications = mapIncidentReportedToNotifications(parsed.data, recipientUserIds);
      await Promise.all(
        notifications.map((notification) =>
          this.notificationsService.createNotification({
            ...notification,
            dedupeKey: buildNotificationDedupeKey(
              TRIP_INCIDENT_REPORTED_ROUTING_KEY,
              messageId,
              notification.userId,
              notification.type,
            ),
          }),
        ),
      );
      await this.idempotency.markProcessed(TRIP_INCIDENT_REPORTED_ROUTING_KEY, messageId);
      this.logger.info(
        {
          routingKey: TRIP_INCIDENT_REPORTED_ROUTING_KEY,
          messageId,
          notificationCount: notifications.length,
        },
        'Processed incident notification event',
      );
    } catch (error) {
      await this.idempotency.release(TRIP_INCIDENT_REPORTED_ROUTING_KEY, messageId);
      throw error;
    }
  }
}

function buildNotificationDedupeKey(
  routingKey: string,
  messageId: string,
  userId: string,
  type: string,
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
