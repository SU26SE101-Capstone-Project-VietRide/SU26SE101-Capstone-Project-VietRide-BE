import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import { NotificationType } from '../generated/notification-prisma-client';
import { MessageIdempotencyService } from './message-idempotency.service';
import { NotificationsService } from './notifications.service';
import type { OperatorRecipientProvider } from './operator-recipient.provider';
import { BookingTripRecipientProvider } from './booking-trip-recipient.provider';
import { TripAnnouncementRecipientProvider } from './trip-announcement-recipient.provider';
import {
  TRACKING_GPS_OFF_ROUTE_ROUTING_KEY,
  TRIP_BOARDING_STARTED_ROUTING_KEY,
  TRIP_DELAYED_ROUTING_KEY,
  TRIP_INCIDENT_REPORTED_ROUTING_KEY,
  TRIP_CARGO_THRESHOLD_CROSSED_ROUTING_KEY,
  TRIP_STOP_DISABLED_ROUTING_KEY,
  TRIP_ROUTE_CHANGED_ROUTING_KEY,
  TRIP_SCHEDULE_CHANGED_ROUTING_KEY,
  TRIP_TRACKING_ALERT_QUEUE_BINDINGS,
  TRIP_VEHICLE_SWAPPED_ROUTING_KEY,
} from './trip-tracking-alert-events.constants';
import { TripTrackingAlertEventsConsumer } from './trip-tracking-alert-events.consumer';

const USER_ID = '11111111-1111-4111-8111-111111111111';
const SECOND_USER_ID = '22222222-2222-4222-8222-222222222222';
const TRIP_ID = '33333333-3333-4333-8333-333333333333';
const OPERATOR_ID = '44444444-4444-4444-8444-444444444444';
const INCIDENT_ID = '55555555-5555-4555-8555-555555555555';
const REPORTER_ID = '66666666-6666-4666-8666-666666666666';
const EVENT_ID = '77777777-7777-4777-8777-777777777777';
const MESSAGE_ID = 'trip-alert-message-1';

describe('TripTrackingAlertEventsConsumer subscribes all phase 5 routing keys', () => {
  let rabbitConsumer: jest.Mocked<RabbitMqConsumer>;
  let idempotency: jest.Mocked<MessageIdempotencyService>;
  let notificationsService: jest.Mocked<NotificationsService>;
  let operatorRecipients: jest.Mocked<OperatorRecipientProvider>;
  let bookingTripRecipients: jest.Mocked<BookingTripRecipientProvider>;
  let tripRecipients: jest.Mocked<TripAnnouncementRecipientProvider>;
  let consumer: TripTrackingAlertEventsConsumer;

  beforeEach(() => {
    rabbitConsumer = {
      subscribe: jest.fn(),
    } as unknown as jest.Mocked<RabbitMqConsumer>;
    idempotency = {
      begin: jest.fn(),
      markProcessed: jest.fn(),
      release: jest.fn(),
    } as unknown as jest.Mocked<MessageIdempotencyService>;
    notificationsService = {
      createNotification: jest.fn(),
    } as unknown as jest.Mocked<NotificationsService>;
    operatorRecipients = {
      resolveOperatorRecipientUserIds: jest.fn(),
    } as jest.Mocked<OperatorRecipientProvider>;
    bookingTripRecipients = {
      resolveTripPassengerUserIds: jest.fn(),
      resolveAffectedTripPassengerUserIds: jest.fn(),
    } as unknown as jest.Mocked<BookingTripRecipientProvider>;
    tripRecipients = {
      getTripRecipientSnapshot: jest.fn(),
      resolveTripCrewUserIds: jest.fn(),
    } as unknown as jest.Mocked<TripAnnouncementRecipientProvider>;
    consumer = new TripTrackingAlertEventsConsumer(
      rabbitConsumer,
      idempotency,
      notificationsService,
      operatorRecipients,
      bookingTripRecipients,
      tripRecipients,
    );
  });

  it('subscribes all phase 5 routing keys', async () => {
    await consumer.onModuleInit();

    expect(rabbitConsumer.subscribe).toHaveBeenCalledTimes(
      TRIP_TRACKING_ALERT_QUEUE_BINDINGS.length,
    );
    for (const binding of TRIP_TRACKING_ALERT_QUEUE_BINDINGS) {
      expect(rabbitConsumer.subscribe).toHaveBeenCalledWith(
        binding.queue,
        binding.routingKey,
        expect.any(Function),
        { prefetch: 1, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000 },
      );
    }

    expect(rabbitConsumer.subscribe).toHaveBeenCalledWith(
      'notification:booking-stop-disabled-affected',
      TRIP_STOP_DISABLED_ROUTING_KEY,
      expect.any(Function),
      { prefetch: 1, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000 },
    );
    expect(TRIP_TRACKING_ALERT_QUEUE_BINDINGS).toContainEqual({
      queue: 'notification:trip-vehicle-swapped-crew',
      routingKey: TRIP_VEHICLE_SWAPPED_ROUTING_KEY,
    });
    expect(TRIP_TRACKING_ALERT_QUEUE_BINDINGS).toContainEqual({
      queue: 'notification:trip-schedule-changed-crew',
      routingKey: TRIP_SCHEDULE_CHANGED_ROUTING_KEY,
    });
    expect(TRIP_TRACKING_ALERT_QUEUE_BINDINGS).not.toEqual(
      expect.arrayContaining([expect.objectContaining({ routingKey: 'trip.trip.cancelled' })]),
    );

    idempotency.begin.mockResolvedValueOnce('acquired').mockResolvedValueOnce('duplicate');
    const stopDisabledEvent = {
      eventId: EVENT_ID,
      occurredAt: '2026-07-18T03:00:00Z',
      eventType: TRIP_STOP_DISABLED_ROUTING_KEY,
      stopId: '88888888-8888-4888-8888-888888888888',
      recipientUserIds: [USER_ID],
      affectedBookingCount: 1,
    };
    await consumer.handle(
      TRIP_STOP_DISABLED_ROUTING_KEY,
      stopDisabledEvent,
      createMessage(EVENT_ID),
    );
    await consumer.handle(
      TRIP_STOP_DISABLED_ROUTING_KEY,
      stopDisabledEvent,
      createMessage(EVENT_ID),
    );

    expect(notificationsService.createNotification).toHaveBeenCalledTimes(1);
    expect(notificationsService.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.STOP_DISABLED,
        dedupeKey: `${TRIP_STOP_DISABLED_ROUTING_KEY}:${EVENT_ID}:${USER_ID}:${NotificationType.STOP_DISABLED}`,
      }),
    );
  });

  it('creates delayed notification for a new valid message', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    tripRecipients.getTripRecipientSnapshot.mockResolvedValue({
      operatorId: OPERATOR_ID,
      crewUserIds: [],
    });
    bookingTripRecipients.resolveTripPassengerUserIds.mockResolvedValue([USER_ID]);
    operatorRecipients.resolveOperatorRecipientUserIds.mockResolvedValue([]);
    notificationsService.createNotification.mockResolvedValue({
      id: '66666666-6666-4666-8666-666666666666',
      userId: USER_ID,
      type: NotificationType.TRIP_DELAYED,
      title: 'Chuyến xe bị trễ',
      body: 'Chuyen xe bi tre',
      data: { tripId: TRIP_ID },
      action: { type: 'OPEN_TRIP_TRACKING', params: { tripId: TRIP_ID } },
      readAt: null,
      createdAt: '2026-06-01T10:00:00.000Z',
    });

    await consumer.handle(
      TRIP_DELAYED_ROUTING_KEY,
      legacyDelayedPayload(15),
      createMessage(MESSAGE_ID),
    );

    expect(notificationsService.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.TRIP_DELAYED,
        dedupeKey: `${TRIP_DELAYED_ROUTING_KEY}:${MESSAGE_ID}:${USER_ID}:${NotificationType.TRIP_DELAYED}`,
      }),
    );
    expect(idempotency.markProcessed).toHaveBeenCalledWith(TRIP_DELAYED_ROUTING_KEY, MESSAGE_ID);
  });

  it('resolves boarding reminder recipients from Booking and deduplicates passengers', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    bookingTripRecipients.resolveTripPassengerUserIds.mockResolvedValue([
      USER_ID,
      USER_ID,
      SECOND_USER_ID,
    ]);

    await consumer.handle(
      TRIP_BOARDING_STARTED_ROUTING_KEY,
      { tripId: TRIP_ID, boardingStartedAt: '2026-07-27T03:00:00Z' },
      createMessage(MESSAGE_ID),
    );

    expect(bookingTripRecipients.resolveTripPassengerUserIds).toHaveBeenCalledWith(TRIP_ID);
    expect(notificationsService.createNotification).toHaveBeenCalledTimes(2);
    expect(notificationsService.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.TRIP_BOARDING_REMINDER,
        dedupeKey: `${TRIP_BOARDING_STARTED_ROUTING_KEY}:${MESSAGE_ID}:${USER_ID}:${NotificationType.TRIP_BOARDING_REMINDER}`,
      }),
    );
    expect(idempotency.markProcessed).toHaveBeenCalledWith(
      TRIP_BOARDING_STARTED_ROUTING_KEY,
      MESSAGE_ID,
    );
  });

  it('releases boarding processing lock when Booking recipient lookup fails', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    bookingTripRecipients.resolveTripPassengerUserIds.mockRejectedValue(
      new Error('BOOKING_UNAVAILABLE'),
    );

    await expect(
      consumer.handle(
        TRIP_BOARDING_STARTED_ROUTING_KEY,
        { tripId: TRIP_ID, boardingStartedAt: '2026-07-27T03:00:00Z' },
        createMessage(MESSAGE_ID),
      ),
    ).rejects.toThrow('BOOKING_UNAVAILABLE');

    expect(idempotency.release).toHaveBeenCalledWith(
      TRIP_BOARDING_STARTED_ROUTING_KEY,
      MESSAGE_ID,
    );
    expect(idempotency.markProcessed).not.toHaveBeenCalled();
  });

  it('resolves delayed recipients as active passengers plus operator admins', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    tripRecipients.getTripRecipientSnapshot.mockResolvedValue({
      operatorId: OPERATOR_ID,
      crewUserIds: [],
    });
    bookingTripRecipients.resolveTripPassengerUserIds.mockResolvedValue([USER_ID]);
    operatorRecipients.resolveOperatorRecipientUserIds.mockResolvedValue([
      SECOND_USER_ID,
      USER_ID,
    ]);

    await consumer.handle(
      TRIP_DELAYED_ROUTING_KEY,
      legacyDelayedPayload(20),
      createMessage(MESSAGE_ID),
    );

    expect(notificationsService.createNotification).toHaveBeenCalledTimes(2);
    expect(notificationsService.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({ userId: USER_ID, type: NotificationType.TRIP_DELAYED }),
    );
    expect(notificationsService.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({ userId: SECOND_USER_ID, type: NotificationType.TRIP_DELAYED }),
    );
  });

  it('resolves off-route recipients as current crew plus operator admins', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    tripRecipients.getTripRecipientSnapshot.mockResolvedValue({
      operatorId: OPERATOR_ID,
      crewUserIds: [USER_ID],
    });
    operatorRecipients.resolveOperatorRecipientUserIds.mockResolvedValue([SECOND_USER_ID]);

    await consumer.handle(
      TRACKING_GPS_OFF_ROUTE_ROUTING_KEY,
      legacyOffRoutePayload(),
      createMessage(MESSAGE_ID),
    );

    expect(notificationsService.createNotification).toHaveBeenCalledTimes(2);
    expect(bookingTripRecipients.resolveTripPassengerUserIds).not.toHaveBeenCalled();
  });

  it('notifies only current crew for schedule changes', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    tripRecipients.resolveTripCrewUserIds.mockResolvedValue([USER_ID]);

    await consumer.handle(
      TRIP_SCHEDULE_CHANGED_ROUTING_KEY,
      {
        eventId: EVENT_ID,
        occurredAt: '2026-07-27T03:00:00Z',
        tripId: TRIP_ID,
        operatorId: OPERATOR_ID,
        oldDeparture: '2026-07-28T03:00:00Z',
        newDeparture: '2026-07-28T04:00:00Z',
        severity: 'MEDIUM',
      },
      createMessage(MESSAGE_ID),
    );

    expect(tripRecipients.resolveTripCrewUserIds).toHaveBeenCalledWith(TRIP_ID, OPERATOR_ID);
    expect(bookingTripRecipients.resolveTripPassengerUserIds).not.toHaveBeenCalled();
    expect(operatorRecipients.resolveOperatorRecipientUserIds).not.toHaveBeenCalled();
    expect(notificationsService.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.TRIP_SCHEDULE_CHANGED,
      }),
    );
  });

  it('notifies current crew and only affected passengers for route changes', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    tripRecipients.resolveTripCrewUserIds.mockResolvedValue([USER_ID]);
    bookingTripRecipients.resolveAffectedTripPassengerUserIds.mockResolvedValue([
      SECOND_USER_ID,
    ]);
    const bookingId = '88888888-8888-4888-8888-888888888888';

    await consumer.handle(
      TRIP_ROUTE_CHANGED_ROUTING_KEY,
      canonicalRouteChangedPayload(bookingId),
      createMessage(MESSAGE_ID),
    );

    expect(bookingTripRecipients.resolveAffectedTripPassengerUserIds).toHaveBeenCalledWith(
      TRIP_ID,
      [bookingId],
    );
    expect(notificationsService.createNotification).toHaveBeenCalledTimes(2);
  });

  it('deduplicates duplicate cargo delivery by message id', async () => {
    idempotency.begin.mockResolvedValueOnce('acquired').mockResolvedValueOnce('duplicate');
    operatorRecipients.resolveOperatorRecipientUserIds.mockResolvedValue([
      USER_ID,
      SECOND_USER_ID,
      USER_ID,
    ]);
    notificationsService.createNotification.mockResolvedValue({} as never);
    const payload = cargoPayload();
    await consumer.handle(TRIP_CARGO_THRESHOLD_CROSSED_ROUTING_KEY, payload, createMessage(EVENT_ID));
    await consumer.handle(TRIP_CARGO_THRESHOLD_CROSSED_ROUTING_KEY, payload, createMessage(EVENT_ID));
    expect(operatorRecipients.resolveOperatorRecipientUserIds).toHaveBeenCalledWith(OPERATOR_ID);
    expect(notificationsService.createNotification).toHaveBeenCalledTimes(2);
    expect(notificationsService.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({ userId: USER_ID }),
    );
    expect(notificationsService.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({ userId: SECOND_USER_ID }),
    );
  });

  it('finalizes malformed cargo payload without persistence', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    await expect(
      consumer.handle(TRIP_CARGO_THRESHOLD_CROSSED_ROUTING_KEY, { eventId: EVENT_ID }, createMessage(EVENT_ID)),
    ).resolves.toBeUndefined();
    expect(notificationsService.createNotification).not.toHaveBeenCalled();
    expect(idempotency.markProcessed).toHaveBeenCalledWith(TRIP_CARGO_THRESHOLD_CROSSED_ROUTING_KEY, EVENT_ID);
  });

  it('releases the processing lock after transient recipient or persistence failure', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    operatorRecipients.resolveOperatorRecipientUserIds.mockRejectedValue(new Error('IDENTITY_UNAVAILABLE'));
    await expect(consumer.handle(TRIP_CARGO_THRESHOLD_CROSSED_ROUTING_KEY, cargoPayload(), createMessage(EVENT_ID))).rejects.toThrow('IDENTITY_UNAVAILABLE');
    expect(idempotency.release).toHaveBeenCalledWith(TRIP_CARGO_THRESHOLD_CROSSED_ROUTING_KEY, EVENT_ID);
  });

  it('skips duplicate delayed message id', async () => {
    idempotency.begin.mockResolvedValue('duplicate');

    await consumer.handle(
      TRIP_DELAYED_ROUTING_KEY,
      {
        userId: USER_ID,
        tripId: TRIP_ID,
      },
      createMessage(MESSAGE_ID),
    );

    expect(notificationsService.createNotification).not.toHaveBeenCalled();
  });

  it('skips duplicate off-route message id', async () => {
    idempotency.begin.mockResolvedValue('duplicate');

    await consumer.handle(
      TRACKING_GPS_OFF_ROUTE_ROUTING_KEY,
      {
        userId: USER_ID,
        tripId: TRIP_ID,
        durationSeconds: 120,
      },
      createMessage(MESSAGE_ID),
    );

    expect(notificationsService.createNotification).not.toHaveBeenCalled();
  });

  it('drops malformed payload without rethrowing', async () => {
    idempotency.begin.mockResolvedValue('acquired');

    await expect(
      consumer.handle(
        TRIP_DELAYED_ROUTING_KEY,
        {
          tripId: 'not-a-uuid',
          delayMinutes: 15,
        },
        createMessage(MESSAGE_ID),
      ),
    ).resolves.toBeUndefined();
    expect(notificationsService.createNotification).not.toHaveBeenCalled();
    expect(idempotency.markProcessed).toHaveBeenCalledWith(TRIP_DELAYED_ROUTING_KEY, MESSAGE_ID);
  });

  it.each([
    TRIP_DELAYED_ROUTING_KEY,
    TRACKING_GPS_OFF_ROUTE_ROUTING_KEY,
  ] as const)('does not resolve recipients for an incomplete %s fact', async (routingKey) => {
    idempotency.begin.mockResolvedValue('acquired');

    await expect(
      consumer.handle(routingKey, { tripId: TRIP_ID }, createMessage(MESSAGE_ID)),
    ).resolves.toBeUndefined();

    expect(tripRecipients.getTripRecipientSnapshot).not.toHaveBeenCalled();
    expect(bookingTripRecipients.resolveTripPassengerUserIds).not.toHaveBeenCalled();
    expect(operatorRecipients.resolveOperatorRecipientUserIds).not.toHaveBeenCalled();
    expect(notificationsService.createNotification).not.toHaveBeenCalled();
    expect(idempotency.markProcessed).toHaveBeenCalledWith(routingKey, MESSAGE_ID);
  });

  it('resolves and deduplicates operator admins using payload event id', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    operatorRecipients.resolveOperatorRecipientUserIds.mockResolvedValue([
      USER_ID,
      USER_ID,
      SECOND_USER_ID,
    ]);
    notificationsService.createNotification.mockResolvedValue({} as never);

    await consumer.handle(
      TRIP_INCIDENT_REPORTED_ROUTING_KEY,
      canonicalIncidentPayload(),
      createMessage(MESSAGE_ID),
    );

    expect(idempotency.begin).toHaveBeenCalledWith(
      TRIP_INCIDENT_REPORTED_ROUTING_KEY,
      EVENT_ID,
      undefined,
    );
    expect(operatorRecipients.resolveOperatorRecipientUserIds).toHaveBeenCalledWith(OPERATOR_ID);
    expect(notificationsService.createNotification).toHaveBeenCalledTimes(2);
    expect(notificationsService.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.INCIDENT_REPORTED,
        dedupeKey: `${TRIP_INCIDENT_REPORTED_ROUTING_KEY}:${EVENT_ID}:${USER_ID}:${NotificationType.INCIDENT_REPORTED}`,
      }),
    );
    expect(idempotency.markProcessed).toHaveBeenCalledWith(
      TRIP_INCIDENT_REPORTED_ROUTING_KEY,
      EVENT_ID,
    );
  });

  it('treats empty operator recipients as a successful no-op', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    operatorRecipients.resolveOperatorRecipientUserIds.mockResolvedValue([]);

    await consumer.handle(
      TRIP_INCIDENT_REPORTED_ROUTING_KEY,
      canonicalIncidentPayload(),
      createMessage(MESSAGE_ID),
    );

    expect(notificationsService.createNotification).not.toHaveBeenCalled();
    expect(idempotency.markProcessed).toHaveBeenCalledWith(
      TRIP_INCIDENT_REPORTED_ROUTING_KEY,
      EVENT_ID,
    );
  });

  it('falls back to broker message id for a legacy incident without event id', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    operatorRecipients.resolveOperatorRecipientUserIds.mockResolvedValue([]);

    await consumer.handle(
      TRIP_INCIDENT_REPORTED_ROUTING_KEY,
      canonicalIncidentPayload({ eventId: undefined }),
      createMessage(MESSAGE_ID),
    );

    expect(idempotency.begin).toHaveBeenCalledWith(
      TRIP_INCIDENT_REPORTED_ROUTING_KEY,
      MESSAGE_ID,
      undefined,
    );
    expect(idempotency.markProcessed).toHaveBeenCalledWith(
      TRIP_INCIDENT_REPORTED_ROUTING_KEY,
      MESSAGE_ID,
    );
  });

  it('marks a malformed incident processed without calling Identity', async () => {
    idempotency.begin.mockResolvedValue('acquired');

    await expect(
      consumer.handle(
        TRIP_INCIDENT_REPORTED_ROUTING_KEY,
        { ...canonicalIncidentPayload(), operatorId: 'invalid' },
        createMessage(MESSAGE_ID),
      ),
    ).resolves.toBeUndefined();

    expect(operatorRecipients.resolveOperatorRecipientUserIds).not.toHaveBeenCalled();
    expect(idempotency.markProcessed).toHaveBeenCalledWith(
      TRIP_INCIDENT_REPORTED_ROUTING_KEY,
      MESSAGE_ID,
    );
  });

  it('releases the incident lock when Identity resolution fails', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    operatorRecipients.resolveOperatorRecipientUserIds.mockRejectedValue(
      new Error('IDENTITY_UNAVAILABLE'),
    );

    await expect(
      consumer.handle(
        TRIP_INCIDENT_REPORTED_ROUTING_KEY,
        canonicalIncidentPayload(),
        createMessage(MESSAGE_ID),
      ),
    ).rejects.toThrow('IDENTITY_UNAVAILABLE');
    expect(idempotency.release).toHaveBeenCalledWith(TRIP_INCIDENT_REPORTED_ROUTING_KEY, EVENT_ID);
    expect(idempotency.markProcessed).not.toHaveBeenCalled();
  });

  it('releases the incident lock when notification persistence fails', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    operatorRecipients.resolveOperatorRecipientUserIds.mockResolvedValue([USER_ID]);
    notificationsService.createNotification.mockRejectedValue(new Error('DB_UNAVAILABLE'));

    await expect(
      consumer.handle(
        TRIP_INCIDENT_REPORTED_ROUTING_KEY,
        canonicalIncidentPayload(),
        createMessage(MESSAGE_ID),
      ),
    ).rejects.toThrow('DB_UNAVAILABLE');
    expect(idempotency.release).toHaveBeenCalledWith(TRIP_INCIDENT_REPORTED_ROUTING_KEY, EVENT_ID);
    expect(idempotency.markProcessed).not.toHaveBeenCalled();
  });
});

function createMessage(messageId: string | undefined): ConsumeMessage {
  return {
    properties: {
      messageId,
      correlationId: undefined,
    },
  } as ConsumeMessage;
}

function canonicalIncidentPayload(
  overrides: Record<string, unknown> = {},
): Record<string, unknown> {
  return {
    eventId: EVENT_ID,
    occurredAt: '2026-07-16T03:00:00Z',
    eventType: TRIP_INCIDENT_REPORTED_ROUTING_KEY,
    incidentId: INCIDENT_ID,
    tripId: TRIP_ID,
    operatorId: OPERATOR_ID,
    reporterUserId: REPORTER_ID,
    category: 'TRAFFIC_JAM',
    reportedAt: '2026-07-16T03:00:00Z',
    ...overrides,
  };
}

function cargoPayload(): Record<string, unknown> {
  return {
    eventId: EVENT_ID,
    occurredAt: '2026-07-16T03:00:00Z',
    tripId: TRIP_ID,
    operatorId: OPERATOR_ID,
    loadedWeightKg: 80,
    maxCargoWeightKg: 100,
    percentFull: 80,
  };
}

function legacyDelayedPayload(delayMinutes: number): Record<string, unknown> {
  return {
    tripId: TRIP_ID,
    stopId: '88888888-8888-4888-8888-888888888888',
    staticEstimatedArrivalTime: '2026-07-27T03:00:00Z',
    dynamicEstimatedArrivalTime: '2026-07-27T04:00:00Z',
    etaNew: '2026-07-27T04:00:00Z',
    delayMinutes,
    detectedAt: '2026-07-27T02:30:00Z',
  };
}

function legacyOffRoutePayload(): Record<string, unknown> {
  return {
    tripId: TRIP_ID,
    latitude: 10.7769,
    longitude: 106.7009,
    distanceMeters: 250,
    durationSeconds: 120,
    detectedAt: '2026-07-27T03:00:00Z',
  };
}

function canonicalRouteChangedPayload(bookingId: string): Record<string, unknown> {
  return {
    eventId: EVENT_ID,
    occurredAt: '2026-07-27T03:00:00Z',
    tripId: TRIP_ID,
    operatorId: OPERATOR_ID,
    tripStatus: 'IN_PROGRESS',
    alternativeRouteId: '99999999-9999-4999-8999-999999999999',
    affectedBookings: [
      {
        bookingId,
        candidateStops: [
          {
            stopId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
            stationId: null,
            stationName: 'Điểm dừng thay thế',
            sequence: 1,
            estimatedArrivalAt: '2026-07-27T04:00:00Z',
          },
        ],
      },
    ],
  };
}
