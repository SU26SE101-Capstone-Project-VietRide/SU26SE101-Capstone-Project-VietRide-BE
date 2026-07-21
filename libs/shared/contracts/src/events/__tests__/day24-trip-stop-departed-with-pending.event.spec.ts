import {
  TRIP_STOP_DEPARTED_WITH_PENDING_ROUTING_KEY,
  TripStopDepartedWithPendingEventSchema,
} from '../../index';

const canonical = {
  eventId: '11111111-1111-4111-8111-111111111111',
  occurredAt: '2026-07-18T10:00:00Z',
  eventType: TRIP_STOP_DEPARTED_WITH_PENDING_ROUTING_KEY,
  tripId: '22222222-2222-4222-8222-222222222222',
  stopId: '33333333-3333-4333-8333-333333333333',
  stopName: 'Ben xe Mien Dong Moi',
  pendingPassengerCount: 2,
  driverUserId: '44444444-4444-4444-8444-444444444444',
  assistantUserId: null,
  departedAt: '2026-07-18T10:00:00Z',
};

describe('Day 24 departed-pending event contract:', () => {
  it('accepts the frozen nullable-assistant payload', () => {
    expect(TripStopDepartedWithPendingEventSchema.parse(canonical)).toEqual(canonical);
    expect(
      TripStopDepartedWithPendingEventSchema.parse({
        ...canonical,
        assistantUserId: '55555555-5555-4555-8555-555555555555',
      }),
    ).toEqual({
      ...canonical,
      assistantUserId: '55555555-5555-4555-8555-555555555555',
    });
  });

  it('rejects missing nullable fields, non-positive counts, non-UTC shapes, and extras', () => {
    const { assistantUserId, ...withoutAssistantField } = canonical;
    void assistantUserId;
    expect(TripStopDepartedWithPendingEventSchema.safeParse(withoutAssistantField).success).toBe(
      false,
    );
    expect(
      TripStopDepartedWithPendingEventSchema.safeParse({
        ...canonical,
        pendingPassengerCount: 0,
      }).success,
    ).toBe(false);
    expect(
      TripStopDepartedWithPendingEventSchema.safeParse({
        ...canonical,
        departedAt: 'not-an-instant',
      }).success,
    ).toBe(false);
    expect(
      TripStopDepartedWithPendingEventSchema.safeParse({
        ...canonical,
        operatorId: canonical.tripId,
      }).success,
    ).toBe(false);
  });
});
