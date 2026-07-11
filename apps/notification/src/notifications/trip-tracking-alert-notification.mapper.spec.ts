import { ZodError } from 'zod';
import { NotificationType } from '../generated/notification-prisma-client';
import {
  TRACKING_GPS_APPROACHING_STOP_ROUTING_KEY,
  TRACKING_GPS_OFF_ROUTE_ROUTING_KEY,
  TRIP_DELAYED_ROUTING_KEY,
  TRIP_ASSIGNED_ROUTING_KEY,
  TRIP_CREW_CHANGED_ROUTING_KEY,
  TRIP_INCIDENT_REPORTED_ROUTING_KEY,
  TRIP_STOP_DISABLED_ROUTING_KEY,
} from './trip-tracking-alert-events.constants';
import { mapTripTrackingAlertToNotifications } from './trip-tracking-alert-notification.mapper';

const USER_ID = '11111111-1111-4111-8111-111111111111';
const SECOND_USER_ID = '22222222-2222-4222-8222-222222222222';
const TRIP_ID = '33333333-3333-4333-8333-333333333333';
const STOP_ID = '44444444-4444-4444-8444-444444444444';
const INCIDENT_ID = '55555555-5555-4555-8555-555555555555';

describe('mapTripTrackingAlertToNotifications', () => {
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
      expect.objectContaining({ userId: '88888888-8888-4888-8888-888888888888', type: NotificationType.TRIP_ASSIGNED }),
      expect.objectContaining({ userId: USER_ID, type: NotificationType.TRIP_ASSIGNMENT_REMOVED }),
    ]);
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
        title: 'Xe sap den diem don',
        body: 'Xe cua ban se den Ben xe Da Lat trong khoang 30 phut.',
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
        title: 'Xe dang den rat gan',
        body: 'Xe cua ban sap den Ben xe Da Lat! Vui long ra diem don.',
      }),
    ]);
  });

  it('maps delayed event for each recipient', () => {
    expect(
      mapTripTrackingAlertToNotifications(TRIP_DELAYED_ROUTING_KEY, {
        userIds: [USER_ID, SECOND_USER_ID],
        tripId: TRIP_ID,
        routeName: 'Sai Gon - Da Lat',
        delayMinutes: 20,
      }),
    ).toEqual([
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.TRIP_DELAYED,
        title: 'Chuyen xe bi tre',
        body: 'Chuyen Sai Gon - Da Lat dang bi tre. Du kien tre 20 phut.',
      }),
      expect.objectContaining({
        userId: SECOND_USER_ID,
        type: NotificationType.TRIP_DELAYED,
      }),
    ]);
  });

  it('maps off-route alert', () => {
    expect(
      mapTripTrackingAlertToNotifications(TRACKING_GPS_OFF_ROUTE_ROUTING_KEY, {
        recipientUserIds: [USER_ID],
        tripId: TRIP_ID,
        durationSeconds: 180,
      }),
    ).toEqual([
      expect.objectContaining({
        type: NotificationType.OFF_ROUTE_ALERT,
        title: 'Canh bao xe lech lo trinh',
        data: expect.objectContaining({
          tripId: TRIP_ID,
          durationSeconds: 180,
        }),
      }),
    ]);
  });

  it('maps incident reported event', () => {
    expect(
      mapTripTrackingAlertToNotifications(TRIP_INCIDENT_REPORTED_ROUTING_KEY, {
        userId: USER_ID,
        tripId: TRIP_ID,
        incidentId: INCIDENT_ID,
        category: 'SAFETY',
      }),
    ).toEqual([
      expect.objectContaining({
        type: NotificationType.INCIDENT_REPORTED,
        title: 'Co su co tren chuyen xe',
        body: `Chuyen ${TRIP_ID} vua ghi nhan su co: SAFETY.`,
      }),
    ]);
  });

  it('maps stop disabled event for explicit recipients', () => {
    expect(
      mapTripTrackingAlertToNotifications(TRIP_STOP_DISABLED_ROUTING_KEY, {
        userId: USER_ID,
        stopId: STOP_ID,
        stopName: 'Ben xe Da Lat',
        replacedByStopId: '66666666-6666-4666-8666-666666666666',
      }),
    ).toEqual([
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.STOP_DISABLED,
        title: 'Diem dung tam ngung phuc vu',
        body: expect.stringContaining('Ben xe Da Lat'),
        data: expect.objectContaining({
          stopId: STOP_ID,
          replacedByStopId: '66666666-6666-4666-8666-666666666666',
        }),
      }),
    ]);
  });

  it('rejects payload without recipient user id', () => {
    expect(() =>
      mapTripTrackingAlertToNotifications(TRIP_DELAYED_ROUTING_KEY, {
        tripId: TRIP_ID,
        delayMinutes: 20,
      }),
    ).toThrow(ZodError);
  });
});
