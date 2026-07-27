import { TRIP_STOP_DEPARTED_WITH_PENDING_ROUTING_KEY } from '@vietride/contracts';
import { NotificationType } from '../generated/notification-prisma-client';
import {
  TRACKING_GPS_APPROACHING_STOP_ROUTING_KEY,
  TRACKING_GPS_OFF_ROUTE_ROUTING_KEY,
  TRIP_DELAYED_ROUTING_KEY,
  TRIP_ASSIGNED_ROUTING_KEY,
  TRIP_CREW_CHANGED_ROUTING_KEY,
  TRIP_INCIDENT_REPORTED_ROUTING_KEY,
  TRIP_STOP_DISABLED_ROUTING_KEY,
  TRIP_VEHICLE_SWAPPED_ROUTING_KEY,
} from './trip-tracking-alert-events.constants';
import {
  IncidentReportedPayloadSchema,
  mapIncidentReportedToNotifications,
  mapTripCargoThresholdCrossedToNotifications,
  mapTripTrackingAlertToNotifications,
} from './trip-tracking-alert-notification.mapper';

const USER_ID = '11111111-1111-4111-8111-111111111111';
const SECOND_USER_ID = '22222222-2222-4222-8222-222222222222';
const TRIP_ID = '33333333-3333-4333-8333-333333333333';
const STOP_ID = '44444444-4444-4444-8444-444444444444';
const INCIDENT_ID = '55555555-5555-4555-8555-555555555555';
const OPERATOR_ID = '66666666-6666-4666-8666-666666666666';
const REPORTER_ID = '77777777-7777-4777-8777-777777777777';

describe('mapTripTrackingAlertToNotifications maps stop disabled event for explicit recipients and maps departed-with-pending warning to assigned driver and assistant only', () => {
  it('maps a valid cargo threshold fact to CARGO_NEAR_FULL_ALERT', () => {
    const notifications = mapTripCargoThresholdCrossedToNotifications(
      {
        eventId: '88888888-8888-4888-8888-888888888888',
        occurredAt: '2026-07-18T03:00:00Z',
        tripId: TRIP_ID,
        operatorId: OPERATOR_ID,
        loadedWeightKg: 80,
        maxCargoWeightKg: 100,
        percentFull: 80,
      }, [USER_ID],
    );
    expect(notifications[0]).toEqual(expect.objectContaining({ userId: USER_ID, type: NotificationType.CARGO_NEAR_FULL_ALERT }));
  });

  it('scopes cargo recipients to active admins of the payload operator', () => {
    const notifications = mapTripCargoThresholdCrossedToNotifications(
      {
        eventId: '88888888-8888-4888-8888-888888888888',
        occurredAt: '2026-07-18T03:00:00Z',
        tripId: TRIP_ID,
        operatorId: OPERATOR_ID,
        loadedWeightKg: 80,
        maxCargoWeightKg: 100,
        percentFull: 80,
      },
      [USER_ID, USER_ID, SECOND_USER_ID],
    );
    expect(notifications.map((notification) => notification.userId)).toEqual([USER_ID, SECOND_USER_ID]);
  });

  it('maps a trip assignment to the driver and assistant', () => {
    const notifications = mapTripTrackingAlertToNotifications(TRIP_ASSIGNED_ROUTING_KEY, {
      tripId: TRIP_ID,
      operatorId: '77777777-7777-4777-8777-777777777777',
      driverUserId: USER_ID,
      assistantUserId: SECOND_USER_ID,
      routeName: 'Sai Gon - Da Lat',
      vehiclePlateNumber: '51B-123.45',
      departureDateTime: '2026-07-12T01:00:00+00:00',
    });

    expect(notifications).toEqual([
      expect.objectContaining({ userId: USER_ID, type: NotificationType.TRIP_ASSIGNED }),
      expect.objectContaining({ userId: SECOND_USER_ID, type: NotificationType.TRIP_ASSIGNED }),
    ]);
  });

  it('maps changed crew to assignments and assignment removals only', () => {
    const notifications = mapTripTrackingAlertToNotifications(TRIP_CREW_CHANGED_ROUTING_KEY, {
      tripId: TRIP_ID,
      operatorId: '77777777-7777-4777-8777-777777777777',
      oldDriverUserId: USER_ID,
      oldAssistantUserId: SECOND_USER_ID,
      driverUserId: SECOND_USER_ID,
      assistantUserId: '88888888-8888-4888-8888-888888888888',
      routeName: 'Sai Gon - Da Lat',
      vehiclePlateNumber: '51B-123.45',
      departureDateTime: '2026-07-12T01:00:00+00:00',
    });

    expect(notifications).toEqual([
      expect.objectContaining({
        userId: '88888888-8888-4888-8888-888888888888',
        type: NotificationType.TRIP_ASSIGNED,
      }),
      expect.objectContaining({ userId: USER_ID, type: NotificationType.TRIP_ASSIGNMENT_REMOVED }),
    ]);
  });

  it('maps a vehicle swap to the assigned crew only', () => {
    const notifications = mapTripTrackingAlertToNotifications(TRIP_VEHICLE_SWAPPED_ROUTING_KEY, {
      eventId: '99999999-9999-4999-8999-999999999999',
      occurredAt: '2026-07-15T01:00:00+00:00',
      tripId: TRIP_ID,
      operatorId: '77777777-7777-4777-8777-777777777777',
      oldVehicleId: '66666666-6666-4666-8666-666666666666',
      newVehicleId: '77777777-7777-4777-8777-777777777778',
      oldVehiclePlateNumber: '51B-111.11',
      newVehiclePlateNumber: '51B-222.22',
      departureDateTime: '2026-07-16T01:00:00+00:00',
      driverUserId: USER_ID,
      assistantUserId: SECOND_USER_ID,
      seatImpacts: [
        {
          bookingId: '88888888-8888-4888-8888-888888888888',
          seatNumbers: ['A01'],
          reason: 'SEAT_REMOVED',
        },
      ],
    });

    expect(notifications).toEqual([
      expect.objectContaining({ userId: USER_ID, type: NotificationType.VEHICLE_SWAPPED }),
      expect.objectContaining({ userId: SECOND_USER_ID, type: NotificationType.VEHICLE_SWAPPED }),
    ]);
    expect(notifications).toHaveLength(2);
    expect(notifications[0]?.data).toEqual(
      expect.objectContaining({
        oldVehiclePlateNumber: '51B-111.11',
        newVehiclePlateNumber: '51B-222.22',
        driverUserId: USER_ID,
        assistantUserId: SECOND_USER_ID,
      }),
    );
  });

  it('does not create a recipient-less vehicle-swap notification', () => {
    expect(
      mapTripTrackingAlertToNotifications(TRIP_VEHICLE_SWAPPED_ROUTING_KEY, {
        eventId: '99999999-9999-4999-8999-999999999999',
        occurredAt: '2026-07-15T01:00:00+00:00',
        tripId: TRIP_ID,
        operatorId: '77777777-7777-4777-8777-777777777777',
        oldVehicleId: '66666666-6666-4666-8666-666666666666',
        newVehicleId: '77777777-7777-4777-8777-777777777778',
        oldVehiclePlateNumber: '51B-111.11',
        newVehiclePlateNumber: '51B-222.22',
        departureDateTime: '2026-07-16T01:00:00+00:00',
        driverUserId: USER_ID,
        assistantUserId: null,
        seatImpacts: [],
      }),
    ).toEqual([expect.objectContaining({ userId: USER_ID })]);
  });

  it('maps approaching stop wave 1 title and body', () => {
    expect(
      mapTripTrackingAlertToNotifications(TRACKING_GPS_APPROACHING_STOP_ROUTING_KEY, {
        userId: USER_ID,
        tripId: TRIP_ID,
        stopId: STOP_ID,
        stopName: 'Ben xe Da Lat',
        wave: 1,
        etaMinutes: 30,
      }),
    ).toEqual([
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.TRIP_VEHICLE_APPROACHING,
        title: 'Xe sắp đến điểm đón',
        body: 'Xe của bạn sẽ đến Ben xe Da Lat trong khoảng 30 phút.',
      }),
    ]);
  });

  it('maps approaching stop wave 2 title and body', () => {
    expect(
      mapTripTrackingAlertToNotifications(TRACKING_GPS_APPROACHING_STOP_ROUTING_KEY, {
        userId: USER_ID,
        tripId: TRIP_ID,
        stopId: STOP_ID,
        stopName: 'Ben xe Da Lat',
        wave: 2,
        etaMinutes: 10,
      }),
    ).toEqual([
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.TRIP_VEHICLE_APPROACHING,
        title: 'Xe đang đến rất gần',
        body: 'Xe của bạn sắp đến Ben xe Da Lat! Vui lòng ra điểm đón.',
      }),
    ]);
  });

  it('maps delayed event for each recipient', () => {
    expect(
      mapTripTrackingAlertToNotifications(
        TRIP_DELAYED_ROUTING_KEY,
        canonicalDelayedPayload(),
        [USER_ID, SECOND_USER_ID],
      ),
    ).toEqual([
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.TRIP_DELAYED,
        title: 'Chuyến xe bị trễ',
        body: `Chuyến ${TRIP_ID} đang bị trễ. Dự kiến trễ 20 phút.`,
      }),
      expect.objectContaining({
        userId: SECOND_USER_ID,
        type: NotificationType.TRIP_DELAYED,
      }),
    ]);
  });

  it('maps off-route alert', () => {
    const notifications = mapTripTrackingAlertToNotifications(
      TRACKING_GPS_OFF_ROUTE_ROUTING_KEY,
      canonicalOffRoutePayload(),
      [USER_ID],
    );

    expect(notifications).toHaveLength(1);
    expect(notifications[0]?.type).toBe(NotificationType.OFF_ROUTE_ALERT);
    expect(notifications[0]?.title).toBe('Cảnh báo xe lệch lộ trình');
    expect(notifications[0]?.data).toMatchObject({ tripId: TRIP_ID, durationSeconds: 180 });
  });

  it('maps canonical incident to deduplicated resolved recipients without sensitive data', () => {
    const payload = IncidentReportedPayloadSchema.parse(canonicalIncidentPayload());

    expect(mapIncidentReportedToNotifications(payload, [USER_ID, USER_ID, SECOND_USER_ID])).toEqual(
      [
        expect.objectContaining({
          userId: USER_ID,
          type: NotificationType.INCIDENT_REPORTED,
          title: 'Có sự cố trên chuyến xe',
          body: `Chuyến ${TRIP_ID} vừa ghi nhận sự cố: TRAFFIC_JAM.`,
          data: {
            incidentId: INCIDENT_ID,
            tripId: TRIP_ID,
            operatorId: OPERATOR_ID,
            reporterUserId: REPORTER_ID,
            category: 'TRAFFIC_JAM',
            reportedAt: '2026-07-16T03:00:00Z',
          },
        }),
        expect.objectContaining({ userId: SECOND_USER_ID }),
      ],
    );
  });

  it('accepts omitted or null optional incident fields without recipient ids', () => {
    expect(
      IncidentReportedPayloadSchema.parse(
        canonicalIncidentPayload({
          eventId: undefined,
          description: null,
          photoUrls: null,
          latitude: null,
          longitude: null,
        }),
      ),
    ).toEqual(expect.objectContaining({ operatorId: OPERATOR_ID, eventId: undefined }));
  });

  it('maps stop disabled event for explicit recipients', () => {
    const notifications = mapTripTrackingAlertToNotifications(TRIP_STOP_DISABLED_ROUTING_KEY, {
      eventId: '88888888-8888-4888-8888-888888888888',
      occurredAt: '2026-07-18T03:00:00Z',
      eventType: TRIP_STOP_DISABLED_ROUTING_KEY,
      stopId: STOP_ID,
      replacedByStopId: '66666666-6666-4666-8666-666666666666',
      recipientUserIds: [USER_ID, SECOND_USER_ID],
      affectedBookingCount: 2,
    });

    expect(notifications).toHaveLength(2);
    expect(notifications[0]?.userId).toBe(USER_ID);
    expect(notifications[0]?.type).toBe(NotificationType.STOP_DISABLED);
    expect(notifications[0]?.title).toBe('Điểm dừng tạm ngưng phục vụ');
    expect(notifications[0]?.body).toContain(STOP_ID);
    expect(notifications[0]?.data).toMatchObject({
      stopId: STOP_ID,
      replacedByStopId: '66666666-6666-4666-8666-666666666666',
      affectedBookingCount: 2,
    });
  });

  it('maps departed-with-pending warning to assigned driver and assistant only', () => {
    const payload = {
      eventId: '88888888-8888-4888-8888-888888888888',
      occurredAt: '2026-07-18T03:00:00Z',
      eventType: TRIP_STOP_DEPARTED_WITH_PENDING_ROUTING_KEY,
      tripId: TRIP_ID,
      stopId: STOP_ID,
      stopName: 'Ben xe Da Lat',
      pendingPassengerCount: 2,
      driverUserId: USER_ID,
      assistantUserId: SECOND_USER_ID,
      departedAt: '2026-07-18T03:00:00Z',
    };

    const notifications = mapTripTrackingAlertToNotifications(
      TRIP_STOP_DEPARTED_WITH_PENDING_ROUTING_KEY,
      payload,
    );

    expect(notifications.map(({ userId }) => userId)).toEqual([USER_ID, SECOND_USER_ID]);
    expect(notifications).toHaveLength(2);
    expect(
      notifications.every(
        ({ type }) => type === NotificationType.DRIVER_STOP_DEPARTED_WITH_PENDING,
      ),
    ).toBe(true);
    expect(notifications).not.toContainEqual(
      expect.objectContaining({ userId: '99999999-9999-4999-8999-999999999999' }),
    );

    expect(
      mapTripTrackingAlertToNotifications(TRIP_STOP_DEPARTED_WITH_PENDING_ROUTING_KEY, {
        ...payload,
        assistantUserId: USER_ID,
      }),
    ).toEqual([
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.DRIVER_STOP_DEPARTED_WITH_PENDING,
      }),
    ]);
    expect(
      mapTripTrackingAlertToNotifications(TRIP_STOP_DEPARTED_WITH_PENDING_ROUTING_KEY, {
        ...payload,
        assistantUserId: null,
      }),
    ).toEqual([
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.DRIVER_STOP_DEPARTED_WITH_PENDING,
      }),
    ]);
  });

  it('maps resolver-supplied recipients without requiring or persisting recipient ids in payload', () => {
    const notifications = mapTripTrackingAlertToNotifications(
      TRIP_DELAYED_ROUTING_KEY,
      canonicalDelayedPayload(),
      [USER_ID],
    );
    expect(notifications).toEqual([
      expect.objectContaining({ userId: USER_ID, type: NotificationType.TRIP_DELAYED }),
    ]);
    expect(notifications[0]?.data).not.toHaveProperty('userIds');
  });
});

function canonicalDelayedPayload(): Record<string, unknown> {
  return {
    eventId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
    occurredAt: '2026-07-16T03:00:00Z',
    tripId: TRIP_ID,
    stopId: STOP_ID,
    stopName: 'Bến xe Đà Lạt',
    delayMinutes: 20,
    etaNew: '2026-07-16T04:00:00Z',
  };
}

function canonicalOffRoutePayload(): Record<string, unknown> {
  return {
    eventId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
    occurredAt: '2026-07-16T03:00:00Z',
    tripId: TRIP_ID,
    durationSeconds: 180,
  };
}

function canonicalIncidentPayload(
  overrides: Record<string, unknown> = {},
): Record<string, unknown> {
  return {
    eventId: '88888888-8888-4888-8888-888888888888',
    occurredAt: '2026-07-16T03:00:00Z',
    eventType: TRIP_INCIDENT_REPORTED_ROUTING_KEY,
    incidentId: INCIDENT_ID,
    tripId: TRIP_ID,
    operatorId: OPERATOR_ID,
    reporterUserId: REPORTER_ID,
    category: 'TRAFFIC_JAM',
    description: 'Không được đưa vào notification data',
    photoUrls: ['https://storage.example/incident.jpg'],
    latitude: 10.7731,
    longitude: 106.7032,
    reportedAt: '2026-07-16T03:00:00Z',
    ...overrides,
  };
}
