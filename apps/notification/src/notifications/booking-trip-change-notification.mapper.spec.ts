import {
  BOOKING_PENDING_ACTION_AUTO_RESOLVED_ROUTING_KEY,
  BOOKING_PENDING_ACTION_REALERTED_ROUTING_KEY,
  BOOKING_ROUTE_CHANGE_AUTO_FALLBACK_APPLIED_ROUTING_KEY,
  BOOKING_SCHEDULE_CHANGE_INFORMATIONAL_ROUTING_KEY,
  BOOKING_SCHEDULE_CHANGE_REQUIRED_ROUTING_KEY,
  BOOKING_SEAT_REASSIGNMENT_REQUIRED_ROUTING_KEY,
} from '@vietride/contracts';
import { ZodError } from 'zod';
import { NotificationType } from '../generated/notification-prisma-client';
import { mapBookingTripChangeToNotification } from './booking-trip-change-notification.mapper';

const EVENT_ID = '11111111-1111-4111-8111-111111111111';
const BOOKING_ID = '22222222-2222-4222-8222-222222222222';
const TRIP_ID = '33333333-3333-4333-8333-333333333333';
const USER_ID = '44444444-4444-4444-8444-444444444444';
const PENDING_ACTION_ID = '55555555-5555-4555-8555-555555555555';
const OCCURRED_AT = '2026-07-15T01:00:00+00:00';
const DEADLINE = '2026-07-15T05:00:00+00:00';
const OLD_DEPARTURE = '2026-07-16T01:00:00+00:00';
const NEW_DEPARTURE = '2026-07-16T02:00:00+00:00';

const common = {
  eventId: EVENT_ID,
  occurredAt: OCCURRED_AT,
  bookingId: BOOKING_ID,
  tripId: TRIP_ID,
  userId: USER_ID,
};

describe('mapBookingTripChangeToNotification Day 24 auto-resolved compatibility', () => {
  it('maps every seat-reassignment field to the passenger notification', () => {
    expect(
      mapBookingTripChangeToNotification(BOOKING_SEAT_REASSIGNMENT_REQUIRED_ROUTING_KEY, {
        ...common,
        pendingActionId: PENDING_ACTION_ID,
        deadline: DEADLINE,
        seatNumbers: ['A01', 'A02'],
        reason: 'SEAT_DISABLED',
      }),
    ).toEqual({
      userId: USER_ID,
      type: NotificationType.VEHICLE_SUBSTITUTED,
      title: 'Cần chọn lại ghế',
      body: `Ghế A01, A02 của bạn trên chuyến ${TRIP_ID} cần được chọn lại.`,
      data: {
        eventId: EVENT_ID,
        occurredAt: OCCURRED_AT,
        bookingId: BOOKING_ID,
        tripId: TRIP_ID,
        pendingActionId: PENDING_ACTION_ID,
        deadline: DEADLINE,
        seatNumbers: ['A01', 'A02'],
        reason: 'SEAT_DISABLED',
      },
    });
  });

  it('maps MINOR informational schedule data without pending-action fields', () => {
    const notification = mapBookingTripChangeToNotification(
      BOOKING_SCHEDULE_CHANGE_INFORMATIONAL_ROUTING_KEY,
      {
        ...common,
        oldDeparture: OLD_DEPARTURE,
        newDeparture: NEW_DEPARTURE,
        severity: 'MINOR',
      },
    );

    expect(notification.userId).toBe(USER_ID);
    expect(notification.type).toBe(NotificationType.TRIP_SCHEDULE_CHANGED);
    expect(notification.data).toMatchObject({
      oldDeparture: OLD_DEPARTURE,
      newDeparture: NEW_DEPARTURE,
      severity: 'MINOR',
    });
    expect(notification.data).not.toHaveProperty('pendingActionId');
    expect(notification.data).not.toHaveProperty('deadline');
  });

  it('maps MEDIUM/MAJOR required schedule fields and rejects MINOR', () => {
    for (const severity of ['MEDIUM', 'MAJOR'] as const) {
      const notification = mapBookingTripChangeToNotification(
        BOOKING_SCHEDULE_CHANGE_REQUIRED_ROUTING_KEY,
        {
          ...common,
          pendingActionId: PENDING_ACTION_ID,
          deadline: DEADLINE,
          oldDeparture: OLD_DEPARTURE,
          newDeparture: NEW_DEPARTURE,
          severity,
        },
      );
      expect(notification.userId).toBe(USER_ID);
      expect(notification.type).toBe(NotificationType.TRIP_SCHEDULE_CHANGED);
      expect(notification.data).toMatchObject({
        pendingActionId: PENDING_ACTION_ID,
        deadline: DEADLINE,
        oldDeparture: OLD_DEPARTURE,
        newDeparture: NEW_DEPARTURE,
        severity,
      });
    }

    expect(() =>
      mapBookingTripChangeToNotification(BOOKING_SCHEDULE_CHANGE_REQUIRED_ROUTING_KEY, {
        ...common,
        pendingActionId: PENDING_ACTION_ID,
        deadline: DEADLINE,
        oldDeparture: OLD_DEPARTURE,
        newDeparture: NEW_DEPARTURE,
        severity: 'MINOR',
      }),
    ).toThrow(ZodError);
  });

  it('maps both re-alert discriminants and rejects mismatched details', () => {
    const seatRealert = mapBookingTripChangeToNotification(
      BOOKING_PENDING_ACTION_REALERTED_ROUTING_KEY,
      {
        ...common,
        pendingActionId: PENDING_ACTION_ID,
        deadline: DEADLINE,
        reason: 'PENDING_SEAT_ASSIGNMENT',
        seatNumbers: ['A01'],
        seatImpactReason: 'SEAT_REMOVED',
      },
    );
    expect(seatRealert.type).toBe(NotificationType.VEHICLE_SUBSTITUTED);
    expect(seatRealert.data).toMatchObject({
      reason: 'PENDING_SEAT_ASSIGNMENT',
      seatNumbers: ['A01'],
      seatImpactReason: 'SEAT_REMOVED',
    });

    const scheduleRealert = mapBookingTripChangeToNotification(
      BOOKING_PENDING_ACTION_REALERTED_ROUTING_KEY,
      {
        ...common,
        pendingActionId: PENDING_ACTION_ID,
        deadline: DEADLINE,
        reason: 'SCHEDULE_CHANGE',
        oldDeparture: OLD_DEPARTURE,
        newDeparture: NEW_DEPARTURE,
        severity: 'MAJOR',
      },
    );
    expect(scheduleRealert.type).toBe(NotificationType.TRIP_SCHEDULE_CHANGED);
    expect(scheduleRealert.data).toMatchObject({ reason: 'SCHEDULE_CHANGE', severity: 'MAJOR' });

    expect(() =>
      mapBookingTripChangeToNotification(BOOKING_PENDING_ACTION_REALERTED_ROUTING_KEY, {
        ...common,
        pendingActionId: PENDING_ACTION_ID,
        deadline: DEADLINE,
        reason: 'PENDING_SEAT_ASSIGNMENT',
        oldDeparture: OLD_DEPARTURE,
        newDeparture: NEW_DEPARTURE,
        severity: 'MEDIUM',
      }),
    ).toThrow(ZodError);
    expect(() =>
      mapBookingTripChangeToNotification(BOOKING_PENDING_ACTION_REALERTED_ROUTING_KEY, {
        ...common,
        pendingActionId: PENDING_ACTION_ID,
        deadline: DEADLINE,
        reason: 'SCHEDULE_CHANGE',
        oldDeparture: OLD_DEPARTURE,
        newDeparture: NEW_DEPARTURE,
        severity: 'MINOR',
      }),
    ).toThrow(ZodError);
  });

  it('rejects informational pending-action fields and recipient-less payloads', () => {
    expect(() =>
      mapBookingTripChangeToNotification(BOOKING_SCHEDULE_CHANGE_INFORMATIONAL_ROUTING_KEY, {
        ...common,
        oldDeparture: OLD_DEPARTURE,
        newDeparture: NEW_DEPARTURE,
        severity: 'MINOR',
        pendingActionId: PENDING_ACTION_ID,
      }),
    ).toThrow(ZodError);

    const { userId: removedUserId, ...withoutUser } = common;
    void removedUserId;
    expect(() =>
      mapBookingTripChangeToNotification(BOOKING_SCHEDULE_CHANGE_INFORMATIONAL_ROUTING_KEY, {
        ...withoutUser,
        oldDeparture: OLD_DEPARTURE,
        newDeparture: NEW_DEPARTURE,
        severity: 'MINOR',
      }),
    ).toThrow(ZodError);
  });

  it('Day 24 auto-resolved compatibility remains schedule-change-only', () => {
    const notification = mapBookingTripChangeToNotification(
      BOOKING_PENDING_ACTION_AUTO_RESOLVED_ROUTING_KEY,
      {
        ...common,
        pendingActionId: PENDING_ACTION_ID,
        resolvedAction: 'ACCEPTED',
        severity: 'MAJOR',
        oldDeparture: OLD_DEPARTURE,
        newDeparture: NEW_DEPARTURE,
      },
    );

    expect(notification).toEqual(
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.TRIP_SCHEDULE_CHANGED,
      }),
    );
    expect(() =>
      mapBookingTripChangeToNotification(BOOKING_PENDING_ACTION_AUTO_RESOLVED_ROUTING_KEY, {
        ...common,
        pendingActionId: PENDING_ACTION_ID,
        resolvedAction: 'AUTO_FALLBACK_DESTINATION',
        affectedField: 'PICKUP',
        disabledStopId: '66666666-6666-4666-8666-666666666666',
        fallbackStationId: '77777777-7777-4777-8777-777777777777',
      }),
    ).toThrow(ZodError);
  });

  it('maps the route-change timeout fallback with shuttle coordination data', () => {
    const notification = mapBookingTripChangeToNotification(
      BOOKING_ROUTE_CHANGE_AUTO_FALLBACK_APPLIED_ROUTING_KEY,
      {
        ...common,
        eventType: BOOKING_ROUTE_CHANGE_AUTO_FALLBACK_APPLIED_ROUTING_KEY,
        pendingActionId: PENDING_ACTION_ID,
        originalStopId: '66666666-6666-4666-8666-666666666666',
        fallbackDestinationStationId: '77777777-7777-4777-8777-777777777777',
        shuttleRequired: true,
        resolvedAction: 'AUTO_FALLBACK_DESTINATION',
      },
    );

    expect(notification.userId).toBe(USER_ID);
    expect(notification.type).toBe(NotificationType.TRIP_ROUTE_CHANGED);
    expect(notification.data).toMatchObject({
      originalStopId: '66666666-6666-4666-8666-666666666666',
      fallbackDestinationStationId: '77777777-7777-4777-8777-777777777777',
      shuttleRequired: true,
      resolvedAction: 'AUTO_FALLBACK_DESTINATION',
    });
  });
});
