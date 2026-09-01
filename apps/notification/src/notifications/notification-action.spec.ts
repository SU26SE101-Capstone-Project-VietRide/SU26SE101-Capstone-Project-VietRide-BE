import { NotificationType } from '../generated/notification-prisma-client';
import { resolveNotificationAction } from './notification-action';

const BOOKING_ID = '11111111-1111-4111-8111-111111111111';
const TRIP_ID = '22222222-2222-4222-8222-222222222222';
const PARCEL_ID = '33333333-3333-4333-8333-333333333333';
const SHUTTLE_TRIP_ID = '44444444-4444-4444-8444-444444444444';
const APPROVAL_REQUEST_ID = '55555555-5555-4555-8555-555555555555';

describe('resolveNotificationAction', () => {
  it.each([
    NotificationType.BOOKING_CONFIRMED,
    NotificationType.BOOKING_CANCELLED,
    NotificationType.BOOKING_DISRUPTED,
    NotificationType.PASSENGER_NO_SHOW,
  ])('maps %s to booking detail', (type) => {
    expect(resolveNotificationAction(type, { bookingId: BOOKING_ID })).toEqual({
      type: 'OPEN_BOOKING_DETAIL',
      params: { bookingId: BOOKING_ID },
    });
  });

  it('maps crew booking creation to the trip booking screen', () => {
    expect(
      resolveNotificationAction(NotificationType.BOOKING_CREATED, {
        bookingId: BOOKING_ID,
        tripId: TRIP_ID,
      }),
    ).toEqual({
      type: 'OPEN_CREW_TRIP_BOOKING',
      params: { tripId: TRIP_ID, bookingId: BOOKING_ID },
    });
  });

  it('preserves the legacy driver deep-link meaning for crew cancellation', () => {
    expect(
      resolveNotificationAction(NotificationType.BOOKING_CANCELLED, {
        bookingId: BOOKING_ID,
        tripId: TRIP_ID,
        deepLink: `vietride://driver/trips/${TRIP_ID}/bookings/${BOOKING_ID}`,
      }),
    ).toEqual({
      type: 'OPEN_CREW_TRIP_BOOKING',
      params: { tripId: TRIP_ID, bookingId: BOOKING_ID },
    });
  });

  it.each([
    NotificationType.TRIP_BOARDING_REMINDER,
    NotificationType.TRIP_VEHICLE_APPROACHING,
    NotificationType.TRIP_DELAYED,
    NotificationType.TRIP_DELAYED_ALERT,
    NotificationType.OFF_ROUTE_ALERT,
  ])('maps %s to trip tracking', (type) => {
    expect(resolveNotificationAction(type, { tripId: TRIP_ID })).toEqual({
      type: 'OPEN_TRIP_TRACKING',
      params: { tripId: TRIP_ID },
    });
  });

  it.each([
    NotificationType.TRIP_ROUTE_CHANGED,
    NotificationType.TRIP_SCHEDULE_CHANGED,
    NotificationType.TRIP_CANCELLED,
    NotificationType.TRIP_DISRUPTED,
    NotificationType.VEHICLE_SWAPPED,
    NotificationType.INCIDENT_REPORTED,
    NotificationType.CARGO_NEAR_FULL_ALERT,
    NotificationType.TRIP_ASSIGNED,
    NotificationType.DRIVER_SCHEDULE_EDITED,
    NotificationType.DRIVER_STOP_DEPARTED_WITH_PENDING,
  ])('maps %s to trip detail', (type) => {
    expect(resolveNotificationAction(type, { tripId: TRIP_ID })).toEqual({
      type: 'OPEN_TRIP_DETAIL',
      params: { tripId: TRIP_ID },
    });
  });

  it('maps passenger vehicle substitution to booking detail and gives bookingId precedence', () => {
    expect(
      resolveNotificationAction(NotificationType.VEHICLE_SUBSTITUTED, {
        bookingId: BOOKING_ID,
        tripId: TRIP_ID,
      }),
    ).toEqual({ type: 'OPEN_BOOKING_DETAIL', params: { bookingId: BOOKING_ID } });
  });

  it('keeps the trip-detail fallback for vehicle substitution without booking context', () => {
    expect(resolveNotificationAction(NotificationType.VEHICLE_SUBSTITUTED, { tripId: TRIP_ID })).toEqual({
      type: 'OPEN_TRIP_DETAIL',
      params: { tripId: TRIP_ID },
    });
  });

  it('maps stop-disabled to booking first, then trip', () => {
    expect(
      resolveNotificationAction(NotificationType.STOP_DISABLED, {
        bookingId: BOOKING_ID,
        tripId: TRIP_ID,
      }),
    ).toEqual({ type: 'OPEN_BOOKING_DETAIL', params: { bookingId: BOOKING_ID } });
    expect(resolveNotificationAction(NotificationType.STOP_DISABLED, { tripId: TRIP_ID })).toEqual({
      type: 'OPEN_TRIP_DETAIL',
      params: { tripId: TRIP_ID },
    });
  });

  it('accepts nullable legacy sibling identifiers without losing a valid action target', () => {
    expect(
      resolveNotificationAction(NotificationType.BOOKING_CONFIRMED, {
        bookingId: BOOKING_ID,
        tripId: null,
        deepLink: null,
      }),
    ).toEqual({ type: 'OPEN_BOOKING_DETAIL', params: { bookingId: BOOKING_ID } });
  });

  it.each([
    NotificationType.PARCEL_RESERVED,
    NotificationType.PARCEL_LOADED,
    NotificationType.PARCEL_IN_TRANSIT,
    NotificationType.PARCEL_DELIVERED_PENDING_CONFIRM,
    NotificationType.PARCEL_REJECTED,
    NotificationType.PARCEL_RETURNED,
    NotificationType.PARCEL_REVIEW_REQUESTED,
    NotificationType.PARCEL_REVIEW_APPROVED,
    NotificationType.PARCEL_FINAL_PAYMENT_REQUIRED,
    NotificationType.PARCEL_SETTLEMENT_RECOVERED,
  ])('maps %s to parcel detail', (type) => {
    expect(resolveNotificationAction(type, { parcelId: PARCEL_ID })).toEqual({
      type: 'OPEN_PARCEL_DETAIL',
      params: { parcelId: PARCEL_ID },
    });
  });

  it('maps a Parcel approval request to the native approval screen', () => {
    expect(
      resolveNotificationAction(NotificationType.PARCEL_APPROVAL_REQUESTED, {
        requestId: APPROVAL_REQUEST_ID,
        requestType: 'CUSTODY_EXCEPTION',
        parcelId: PARCEL_ID,
      }),
    ).toEqual({
      type: 'OPEN_PARCEL_APPROVAL',
      params: { requestId: APPROVAL_REQUEST_ID, requestType: 'CUSTODY_EXCEPTION' },
    });
  });

  it.each([
    NotificationType.WALLET_CREDITED,
    NotificationType.WALLET_DEBITED,
    NotificationType.BOOKING_REFUNDED,
  ])('maps %s to wallet', (type) => {
    expect(resolveNotificationAction(type, null)).toEqual({
      type: 'OPEN_WALLET',
      params: {},
    });
  });

  it.each([
    NotificationType.SUBSCRIPTION_LIMIT_EXCEEDED,
    NotificationType.SUBSCRIPTION_USAGE_WARNING,
    NotificationType.SUBSCRIPTION_TRIAL_EXPIRING,
    NotificationType.SUBSCRIPTION_EXPIRED,
    NotificationType.SUBSCRIPTION_APPROVED,
    NotificationType.SUBSCRIPTION_PAYMENT_PENDING_WARN,
    NotificationType.SUBSCRIPTION_PAYMENT_AUTO_REVERTED,
  ])('maps %s to subscription', (type) => {
    expect(resolveNotificationAction(type, null)).toEqual({
      type: 'OPEN_SUBSCRIPTION',
      params: {},
    });
  });

  it.each([
    NotificationType.SHUTTLE_ASSIGNED,
    NotificationType.SHUTTLE_CANCELLED,
    NotificationType.SHUTTLE_PICKED_UP,
    NotificationType.SHUTTLE_DELIVERED,
    NotificationType.SHUTTLE_NO_SHOW,
    NotificationType.SHUTTLE_COMPLETED,
    NotificationType.SHUTTLE_WARNING,
    NotificationType.SHUTTLE_STARTED,
    NotificationType.SHUTTLE_REASSIGNED,
    NotificationType.SHUTTLE_UNASSIGNED,
  ])('maps %s to shuttle tracking', (type) => {
    expect(resolveNotificationAction(type, { shuttleTripId: SHUTTLE_TRIP_ID })).toEqual({
      type: 'OPEN_SHUTTLE_TRACKING',
      params: { shuttleTripId: SHUTTLE_TRIP_ID },
    });
  });

  it('preserves booking and pickup context for Shuttle tracking navigation', () => {
    expect(
      resolveNotificationAction(NotificationType.SHUTTLE_REASSIGNED, {
        shuttleTripId: SHUTTLE_TRIP_ID,
        bookingId: BOOKING_ID,
        pickupOrder: 2,
      }),
    ).toEqual({
      type: 'OPEN_SHUTTLE_TRACKING',
      params: { shuttleTripId: SHUTTLE_TRIP_ID, bookingId: BOOKING_ID, pickupOrder: 2 },
    });
  });

  it('falls back from unfulfilled shuttle to booking detail', () => {
    expect(
      resolveNotificationAction(NotificationType.SHUTTLE_UNFULFILLED, {
        bookingId: BOOKING_ID,
      }),
    ).toEqual({ type: 'OPEN_BOOKING_DETAIL', params: { bookingId: BOOKING_ID } });
  });

  it('opens Booking detail for a passenger whose Shuttle assignment was removed', () => {
    expect(
      resolveNotificationAction(NotificationType.SHUTTLE_UNASSIGNED, {
        bookingId: BOOKING_ID,
      }),
    ).toEqual({ type: 'OPEN_BOOKING_DETAIL', params: { bookingId: BOOKING_ID } });
  });

  it.each([
    NotificationType.VOUCHER_CONSENT_REQUESTED,
    NotificationType.INVOICE_ISSUED,
    NotificationType.PAYOUT_PROCESSED,
    NotificationType.OPERATOR_APPROVED,
    NotificationType.OPERATOR_ANNOUNCEMENT,
    NotificationType.ROUTE_CHANGE_PROPOSAL_CREATED,
    NotificationType.TRIP_ASSIGNMENT_REMOVED,
  ])('returns NONE for intentionally non-navigable %s', (type) => {
    expect(resolveNotificationAction(type, { tripId: TRIP_ID })).toEqual({
      type: 'NONE',
      params: {},
    });
  });

  it.each([null, 'not-an-object', { bookingId: 'not-a-uuid' }, { tripId: 42 }])(
    'returns NONE for missing or malformed action data %#',
    (data) => {
      expect(resolveNotificationAction(NotificationType.BOOKING_CONFIRMED, data)).toEqual({
        type: 'NONE',
        params: {},
      });
    },
  );
});
