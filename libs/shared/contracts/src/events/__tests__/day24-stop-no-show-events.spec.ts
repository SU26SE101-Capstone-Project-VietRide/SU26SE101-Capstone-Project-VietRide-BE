import {
  BOOKING_PASSENGER_NO_SHOW_MARKED_ROUTING_KEY,
  BOOKING_STOP_DISABLED_AFFECTED_ROUTING_KEY,
  BOOKING_STOP_DISABLED_AUTO_FALLBACK_APPLIED_ROUTING_KEY,
  Day24StopNoShowEventSchema,
  TRIP_STOP_DEPARTED_WITH_PENDING_ROUTING_KEY,
  type BookingPassengerNoShowMarkedEvent,
  type BookingStopDisabledAffectedEvent,
  type BookingStopDisabledAutoFallbackAppliedEvent,
  type TripStopDepartedWithPendingEvent,
} from '../../index';

describe('Day 24 event contract:', () => {
  it.each([
    [BOOKING_STOP_DISABLED_AFFECTED_ROUTING_KEY, stopDisabledAffectedEvent()],
    [BOOKING_STOP_DISABLED_AUTO_FALLBACK_APPLIED_ROUTING_KEY, fallbackAppliedEvent()],
    [BOOKING_PASSENGER_NO_SHOW_MARKED_ROUTING_KEY, passengerNoShowEvent()],
    [TRIP_STOP_DEPARTED_WITH_PENDING_ROUTING_KEY, departedWithPendingEvent()],
  ])('parses the exact %s fact', (eventType, event) => {
    expect(Day24StopNoShowEventSchema.parse(event)).toEqual(event);
    expect(event.eventType).toBe(eventType);
  });

  it('rejects an extra field on every frozen fact', () => {
    for (const event of [
      stopDisabledAffectedEvent(),
      fallbackAppliedEvent(),
      passengerNoShowEvent(),
      departedWithPendingEvent(),
    ]) {
      expect(Day24StopNoShowEventSchema.safeParse({ ...event, extraField: true }).success).toBe(
        false,
      );
    }
  });
});

function stopDisabledAffectedEvent(): BookingStopDisabledAffectedEvent {
  return {
    eventId: '11111111-1111-4111-8111-111111111111',
    occurredAt: '2026-07-18T10:00:00+07:00',
    eventType: BOOKING_STOP_DISABLED_AFFECTED_ROUTING_KEY,
    stopId: '22222222-2222-4222-8222-222222222222',
    replacedByStopId: '33333333-3333-4333-8333-333333333333',
    recipientUserIds: ['44444444-4444-4444-8444-444444444444'],
    affectedBookingCount: 1,
  };
}

function fallbackAppliedEvent(): BookingStopDisabledAutoFallbackAppliedEvent {
  return {
    eventId: '11111111-1111-4111-8111-111111111111',
    occurredAt: '2026-07-18T10:00:00+07:00',
    eventType: BOOKING_STOP_DISABLED_AUTO_FALLBACK_APPLIED_ROUTING_KEY,
    bookingId: '22222222-2222-4222-8222-222222222222',
    tripId: '33333333-3333-4333-8333-333333333333',
    userId: '44444444-4444-4444-8444-444444444444',
    pendingActionId: '55555555-5555-4555-8555-555555555555',
    disabledStopId: '66666666-6666-4666-8666-666666666666',
    affectedField: 'PICKUP' as const,
    fallbackStationId: '77777777-7777-4777-8777-777777777777',
    resolvedAction: 'AUTO_FALLBACK_DESTINATION' as const,
  };
}

function passengerNoShowEvent(): BookingPassengerNoShowMarkedEvent {
  return {
    eventId: '11111111-1111-4111-8111-111111111111',
    occurredAt: '2026-07-18T10:00:00+07:00',
    eventType: BOOKING_PASSENGER_NO_SHOW_MARKED_ROUTING_KEY,
    bookingId: '22222222-2222-4222-8222-222222222222',
    tripId: '33333333-3333-4333-8333-333333333333',
    userId: '44444444-4444-4444-8444-444444444444',
    bookingStatus: 'PARTIAL_NO_SHOW' as const,
    newlyNoShowPassengerIds: ['55555555-5555-4555-8555-555555555555'],
    triggerType: 'ALONG_ROUTE' as const,
    pickupStopId: '66666666-6666-4666-8666-666666666666',
  };
}

function departedWithPendingEvent(): TripStopDepartedWithPendingEvent {
  return {
    eventId: '11111111-1111-4111-8111-111111111111',
    occurredAt: '2026-07-18T10:00:00+07:00',
    eventType: TRIP_STOP_DEPARTED_WITH_PENDING_ROUTING_KEY,
    tripId: '22222222-2222-4222-8222-222222222222',
    stopId: '33333333-3333-4333-8333-333333333333',
    stopName: 'Ben xe Mien Dong Moi',
    pendingPassengerCount: 2,
    driverUserId: '44444444-4444-4444-8444-444444444444',
    assistantUserId: null,
    departedAt: '2026-07-18T10:00:00+07:00',
  };
}
