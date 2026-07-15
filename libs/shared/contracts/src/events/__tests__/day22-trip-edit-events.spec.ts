import {
  BOOKING_PENDING_ACTION_REALERTED_ROUTING_KEY,
  BOOKING_SCHEDULE_CHANGE_INFORMATIONAL_ROUTING_KEY,
  BOOKING_SCHEDULE_CHANGE_REQUIRED_ROUTING_KEY,
  BOOKING_SEAT_REASSIGNMENT_REQUIRED_ROUTING_KEY,
  BookingPendingActionRealertedEventSchema,
  BookingScheduleChangeInformationalEventSchema,
  BookingScheduleChangeRequiredEventSchema,
  BookingSeatReassignmentRequiredEventSchema,
  TRIP_CANCELLED_ROUTING_KEY,
  TRIP_SCHEDULE_CHANGED_ROUTING_KEY,
  TRIP_VEHICLE_SWAPPED_ROUTING_KEY,
  TripCancelledEventSchema,
  TripScheduleChangedEventSchema,
  TripVehicleSwappedEventSchema,
} from '../..';

const ids = {
  eventId: '11111111-1111-4111-8111-111111111111',
  tripId: '22222222-2222-4222-8222-222222222222',
  operatorId: '33333333-3333-4333-8333-333333333333',
  bookingId: '44444444-4444-4444-8444-444444444444',
  userId: '55555555-5555-4555-8555-555555555555',
  pendingActionId: '66666666-6666-4666-8666-666666666666',
  oldVehicleId: '77777777-7777-4777-8777-777777777777',
  newVehicleId: '88888888-8888-4888-8888-888888888888',
  driverUserId: '99999999-9999-4999-8999-999999999999',
  assistantUserId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
} as const;

const occurredAt = '2026-07-15T08:00:00+07:00';
const oldDeparture = '2026-07-16T08:00:00+07:00';
const newDeparture = '2026-07-16T09:00:00+07:00';
const deadline = '2026-07-16T07:00:00+07:00';

const pendingActionFields = {
  eventId: ids.eventId,
  occurredAt,
  bookingId: ids.bookingId,
  tripId: ids.tripId,
  userId: ids.userId,
  pendingActionId: ids.pendingActionId,
  deadline,
};

describe('Day-22 Trip edit event contracts', () => {
  it('binds all exact routing keys', () => {
    expect(TRIP_VEHICLE_SWAPPED_ROUTING_KEY).toBe('trip.trip.vehicle_swapped');
    expect(TRIP_SCHEDULE_CHANGED_ROUTING_KEY).toBe('trip.trip.schedule_changed');
    expect(TRIP_CANCELLED_ROUTING_KEY).toBe('trip.trip.cancelled');
    expect(BOOKING_SEAT_REASSIGNMENT_REQUIRED_ROUTING_KEY).toBe(
      'booking.booking.seat_reassignment_required',
    );
    expect(BOOKING_SCHEDULE_CHANGE_INFORMATIONAL_ROUTING_KEY).toBe(
      'booking.booking.schedule_change_informational',
    );
    expect(BOOKING_SCHEDULE_CHANGE_REQUIRED_ROUTING_KEY).toBe(
      'booking.booking.schedule_change_required',
    );
    expect(BOOKING_PENDING_ACTION_REALERTED_ROUTING_KEY).toBe(
      'booking.booking.pending_action_realerted',
    );
  });

  describe('trip.trip.vehicle_swapped', () => {
    const validPayload = {
      eventId: ids.eventId,
      occurredAt,
      tripId: ids.tripId,
      operatorId: ids.operatorId,
      oldVehicleId: ids.oldVehicleId,
      newVehicleId: ids.newVehicleId,
      oldVehiclePlateNumber: '51B-000.01',
      newVehiclePlateNumber: '51B-000.02',
      departureDateTime: oldDeparture,
      driverUserId: ids.driverUserId,
      assistantUserId: ids.assistantUserId,
      seatImpacts: [
        {
          bookingId: ids.bookingId,
          seatNumbers: ['A01'],
          reason: 'SEAT_REMOVED',
        },
      ],
    };

    it('accepts the exact payload and a present nullable assistantUserId', () => {
      expect(TripVehicleSwappedEventSchema.safeParse(validPayload).success).toBe(true);
      expect(
        TripVehicleSwappedEventSchema.safeParse({ ...validPayload, assistantUserId: null }).success,
      ).toBe(true);
    });

    it.each(['SEAT_REMOVED', 'SEAT_DISABLED', 'SEAT_TYPE_DOWNGRADED'])(
      'accepts seat impact reason %s',
      (reason) => {
        expect(
          TripVehicleSwappedEventSchema.safeParse({
            ...validPayload,
            seatImpacts: [{ ...validPayload.seatImpacts[0], reason }],
          }).success,
        ).toBe(true);
      },
    );

    it('rejects omitted assistantUserId', () => {
      const { assistantUserId: _omitted, ...payload } = validPayload;
      expect(TripVehicleSwappedEventSchema.safeParse(payload).success).toBe(false);
    });

    it('rejects extra or renamed fields at every payload level', () => {
      expect(
        TripVehicleSwappedEventSchema.safeParse({ ...validPayload, vehicleId: ids.newVehicleId })
          .success,
      ).toBe(false);
      expect(
        TripVehicleSwappedEventSchema.safeParse({
          ...validPayload,
          seatImpacts: [{ ...validPayload.seatImpacts[0], oldSeatNumbers: ['A01'] }],
        }).success,
      ).toBe(false);
    });

    it('rejects invalid reason and mistyped identifiers or timestamps', () => {
      expect(
        TripVehicleSwappedEventSchema.safeParse({
          ...validPayload,
          seatImpacts: [{ ...validPayload.seatImpacts[0], reason: 'VEHICLE_CHANGED' }],
        }).success,
      ).toBe(false);
      expect(
        TripVehicleSwappedEventSchema.safeParse({ ...validPayload, tripId: 123 }).success,
      ).toBe(false);
      expect(
        TripVehicleSwappedEventSchema.safeParse({
          ...validPayload,
          departureDateTime: 'not-a-datetime',
        }).success,
      ).toBe(false);
    });
  });

  describe('Trip schedule and cancellation facts', () => {
    const schedulePayload = {
      eventId: ids.eventId,
      occurredAt,
      tripId: ids.tripId,
      operatorId: ids.operatorId,
      oldDeparture,
      newDeparture,
      severity: 'MINOR',
    };
    const cancelledPayload = {
      eventId: ids.eventId,
      occurredAt,
      tripId: ids.tripId,
      operatorId: ids.operatorId,
      cancelledAt: occurredAt,
      cancelReason: 'DRIVER_SCHEDULE_DAY_REMOVED',
    };

    it.each(['MINOR', 'MEDIUM', 'MAJOR'])('accepts trip schedule severity %s', (severity) => {
      expect(
        TripScheduleChangedEventSchema.safeParse({ ...schedulePayload, severity }).success,
      ).toBe(true);
    });

    it('rejects missing, extra, invalid, renamed, and mistyped schedule fields', () => {
      const { oldDeparture: _omitted, ...missing } = schedulePayload;
      expect(TripScheduleChangedEventSchema.safeParse(missing).success).toBe(false);
      expect(
        TripScheduleChangedEventSchema.safeParse({ ...schedulePayload, deadline }).success,
      ).toBe(false);
      expect(
        TripScheduleChangedEventSchema.safeParse({ ...schedulePayload, severity: 'CRITICAL' })
          .success,
      ).toBe(false);
      expect(
        TripScheduleChangedEventSchema.safeParse({
          ...schedulePayload,
          oldDeparture: undefined,
          previousDeparture: oldDeparture,
        }).success,
      ).toBe(false);
      expect(
        TripScheduleChangedEventSchema.safeParse({ ...schedulePayload, newDeparture: 123 }).success,
      ).toBe(false);
    });

    it('accepts only the exact cancellation shape', () => {
      expect(TripCancelledEventSchema.safeParse(cancelledPayload).success).toBe(true);
      expect(
        TripCancelledEventSchema.safeParse({ ...cancelledPayload, reason: 'DAY_REMOVED' }).success,
      ).toBe(false);
      expect(
        TripCancelledEventSchema.safeParse({ ...cancelledPayload, cancelledAt: false }).success,
      ).toBe(false);
      const { cancelReason: _omitted, ...missing } = cancelledPayload;
      expect(TripCancelledEventSchema.safeParse(missing).success).toBe(false);
    });
  });

  describe('Booking-owned passenger facts', () => {
    it.each(['SEAT_REMOVED', 'SEAT_DISABLED', 'SEAT_TYPE_DOWNGRADED'])(
      'accepts seat reassignment reason %s with all pending-action fields',
      (reason) => {
        expect(
          BookingSeatReassignmentRequiredEventSchema.safeParse({
            ...pendingActionFields,
            seatNumbers: ['A01'],
            reason,
          }).success,
        ).toBe(true);
      },
    );

    it('rejects missing, extra, renamed, mistyped, or invalid seat reassignment fields', () => {
      const valid = {
        ...pendingActionFields,
        seatNumbers: ['A01'],
        reason: 'SEAT_DISABLED',
      };
      const { pendingActionId: _omitted, ...missing } = valid;
      expect(BookingSeatReassignmentRequiredEventSchema.safeParse(missing).success).toBe(false);
      expect(
        BookingSeatReassignmentRequiredEventSchema.safeParse({ ...valid, severity: 'MAJOR' })
          .success,
      ).toBe(false);
      expect(
        BookingSeatReassignmentRequiredEventSchema.safeParse({
          ...valid,
          seatNumbers: undefined,
          affectedSeats: ['A01'],
        }).success,
      ).toBe(false);
      expect(
        BookingSeatReassignmentRequiredEventSchema.safeParse({ ...valid, seatNumbers: [1] })
          .success,
      ).toBe(false);
      expect(
        BookingSeatReassignmentRequiredEventSchema.safeParse({ ...valid, reason: 'UNKNOWN' })
          .success,
      ).toBe(false);
    });

    it('accepts only the exact MINOR informational shape', () => {
      const informational = {
        eventId: ids.eventId,
        occurredAt,
        bookingId: ids.bookingId,
        tripId: ids.tripId,
        userId: ids.userId,
        oldDeparture,
        newDeparture,
        severity: 'MINOR',
      };
      expect(BookingScheduleChangeInformationalEventSchema.safeParse(informational).success).toBe(
        true,
      );
      expect(
        BookingScheduleChangeInformationalEventSchema.safeParse({
          ...informational,
          pendingActionId: ids.pendingActionId,
        }).success,
      ).toBe(false);
      expect(
        BookingScheduleChangeInformationalEventSchema.safeParse({ ...informational, deadline })
          .success,
      ).toBe(false);
      expect(
        BookingScheduleChangeInformationalEventSchema.safeParse({
          ...informational,
          severity: 'MEDIUM',
        }).success,
      ).toBe(false);
      const { newDeparture: _omitted, ...missing } = informational;
      expect(BookingScheduleChangeInformationalEventSchema.safeParse(missing).success).toBe(false);
      expect(
        BookingScheduleChangeInformationalEventSchema.safeParse({
          ...informational,
          oldDeparture: 123,
        }).success,
      ).toBe(false);
    });

    it.each(['MEDIUM', 'MAJOR'])('accepts required schedule severity %s', (severity) => {
      expect(
        BookingScheduleChangeRequiredEventSchema.safeParse({
          ...pendingActionFields,
          oldDeparture,
          newDeparture,
          severity,
        }).success,
      ).toBe(true);
    });

    it('rejects MINOR, missing pending fields, and extra required-schedule fields', () => {
      const required = {
        ...pendingActionFields,
        oldDeparture,
        newDeparture,
        severity: 'MAJOR',
      };
      expect(
        BookingScheduleChangeRequiredEventSchema.safeParse({ ...required, severity: 'MINOR' })
          .success,
      ).toBe(false);
      const { deadline: _omitted, ...missing } = required;
      expect(BookingScheduleChangeRequiredEventSchema.safeParse(missing).success).toBe(false);
      expect(
        BookingScheduleChangeRequiredEventSchema.safeParse({ ...required, seatNumbers: ['A01'] })
          .success,
      ).toBe(false);
    });

    it('accepts each re-alert discriminant with only its matching detail', () => {
      expect(
        BookingPendingActionRealertedEventSchema.safeParse({
          ...pendingActionFields,
          reason: 'PENDING_SEAT_ASSIGNMENT',
          seatNumbers: ['A01'],
          seatImpactReason: 'SEAT_TYPE_DOWNGRADED',
        }).success,
      ).toBe(true);
      expect(
        BookingPendingActionRealertedEventSchema.safeParse({
          ...pendingActionFields,
          reason: 'SCHEDULE_CHANGE',
          oldDeparture,
          newDeparture,
          severity: 'MEDIUM',
        }).success,
      ).toBe(true);
    });

    it('rejects mismatched, incomplete, or invalid re-alert detail', () => {
      expect(
        BookingPendingActionRealertedEventSchema.safeParse({
          ...pendingActionFields,
          reason: 'PENDING_SEAT_ASSIGNMENT',
          seatNumbers: ['A01'],
          seatImpactReason: 'SEAT_TYPE_DOWNGRADED',
          oldDeparture,
          newDeparture,
          severity: 'MAJOR',
        }).success,
      ).toBe(false);
      expect(
        BookingPendingActionRealertedEventSchema.safeParse({
          ...pendingActionFields,
          reason: 'SCHEDULE_CHANGE',
          oldDeparture,
          newDeparture,
          severity: 'MEDIUM',
          seatNumbers: ['A01'],
          seatImpactReason: 'SEAT_REMOVED',
        }).success,
      ).toBe(false);
      expect(
        BookingPendingActionRealertedEventSchema.safeParse({
          ...pendingActionFields,
          reason: 'SCHEDULE_CHANGE',
          oldDeparture,
          newDeparture,
          severity: 'MINOR',
        }).success,
      ).toBe(false);
      expect(
        BookingPendingActionRealertedEventSchema.safeParse({
          ...pendingActionFields,
          reason: 'PENDING_SEAT_ASSIGNMENT',
          seatNumbers: ['A01'],
          seatImpactReason: 'UNKNOWN',
        }).success,
      ).toBe(false);
    });
  });
});
