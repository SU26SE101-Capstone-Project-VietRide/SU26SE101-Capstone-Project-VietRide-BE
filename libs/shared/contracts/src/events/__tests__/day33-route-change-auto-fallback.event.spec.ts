import {
  BOOKING_ROUTE_CHANGE_AUTO_FALLBACK_APPLIED_ROUTING_KEY,
  BookingRouteChangeAutoFallbackAppliedEventSchema,
} from '../../index';

const event = {
  eventId: '11111111-1111-4111-8111-111111111111',
  occurredAt: '2026-07-23T02:00:00+00:00',
  eventType: BOOKING_ROUTE_CHANGE_AUTO_FALLBACK_APPLIED_ROUTING_KEY,
  bookingId: '22222222-2222-4222-8222-222222222222',
  tripId: '33333333-3333-4333-8333-333333333333',
  userId: '44444444-4444-4444-8444-444444444444',
  pendingActionId: '55555555-5555-4555-8555-555555555555',
  originalStopId: '66666666-6666-4666-8666-666666666666',
  fallbackDestinationStationId: '77777777-7777-4777-8777-777777777777',
  shuttleRequired: true as const,
  resolvedAction: 'AUTO_FALLBACK_DESTINATION' as const,
};

describe('Day 33 route-change auto fallback contract', () => {
  it('accepts only the exact canonical payload', () => {
    expect(BookingRouteChangeAutoFallbackAppliedEventSchema.parse(event)).toEqual(event);
    expect(() =>
      BookingRouteChangeAutoFallbackAppliedEventSchema.parse({
        ...event,
        shuttleRequired: false,
      }),
    ).toThrow();
    expect(() =>
      BookingRouteChangeAutoFallbackAppliedEventSchema.parse({
        ...event,
        refundAmount: 100_000,
      }),
    ).toThrow();
  });
});
