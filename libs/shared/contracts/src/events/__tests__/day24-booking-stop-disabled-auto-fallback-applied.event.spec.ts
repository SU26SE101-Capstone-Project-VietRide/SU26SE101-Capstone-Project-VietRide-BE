import {
  BOOKING_STOP_DISABLED_AUTO_FALLBACK_APPLIED_ROUTING_KEY,
  BookingStopDisabledAutoFallbackAppliedEventSchema,
} from '../../index';

const canonical = {
  eventId: '11111111-1111-4111-8111-111111111111',
  occurredAt: '2026-07-18T10:00:00+07:00',
  eventType: BOOKING_STOP_DISABLED_AUTO_FALLBACK_APPLIED_ROUTING_KEY,
  bookingId: '22222222-2222-4222-8222-222222222222',
  tripId: '33333333-3333-4333-8333-333333333333',
  userId: '44444444-4444-4444-8444-444444444444',
  pendingActionId: '55555555-5555-4555-8555-555555555555',
  disabledStopId: '66666666-6666-4666-8666-666666666666',
  affectedField: 'DROPOFF' as const,
  fallbackStationId: '77777777-7777-4777-8777-777777777777',
  resolvedAction: 'AUTO_FALLBACK_DESTINATION' as const,
};

describe('Day 24 fallback event contract:', () => {
  it('accepts only the exact auto-fallback fact', () => {
    expect(BookingStopDisabledAutoFallbackAppliedEventSchema.parse(canonical)).toEqual(canonical);
    expect(
      BookingStopDisabledAutoFallbackAppliedEventSchema.safeParse({
        ...canonical,
        affectedField: 'ROUTE',
      }).success,
    ).toBe(false);
    expect(
      BookingStopDisabledAutoFallbackAppliedEventSchema.safeParse({
        ...canonical,
        resolvedAction: 'ACCEPTED',
      }).success,
    ).toBe(false);
    expect(
      BookingStopDisabledAutoFallbackAppliedEventSchema.safeParse({
        ...canonical,
        deadline: canonical.occurredAt,
      }).success,
    ).toBe(false);
  });
});
