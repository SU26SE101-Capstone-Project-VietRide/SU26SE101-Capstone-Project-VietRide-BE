import {
  TRIP_CARGO_THRESHOLD_CROSSED_ROUTING_KEY,
  TripCargoThresholdCrossedEventSchema,
  type TripCargoThresholdCrossedEvent,
} from '../../index';

describe('Day 29 trip cargo threshold-crossed contract', () => {
  it('freezes the routing key, exact payload, and shared event identity', () => {
    const event = validEvent();
    const outboxEventId = event.eventId;
    const rabbitMessageId = outboxEventId;

    expect(TRIP_CARGO_THRESHOLD_CROSSED_ROUTING_KEY).toBe('trip.cargo.threshold_crossed');
    expect(TripCargoThresholdCrossedEventSchema.parse(event)).toEqual(event);
    expect(event.eventId).toBe(outboxEventId);
    expect(rabbitMessageId).toBe(event.eventId);
  });

  it.each([
    ['missing field', without(validEvent(), 'operatorId')],
    ['extra field', { ...validEvent(), legacyThreshold: 80 }],
    ['wrong-typed field', { ...validEvent(), loadedWeightKg: '80' }],
  ])('rejects a %s', (_caseName, payload) => {
    expect(TripCargoThresholdCrossedEventSchema.safeParse(payload).success).toBe(false);
  });
});

function validEvent(): TripCargoThresholdCrossedEvent {
  return {
    eventId: '11111111-1111-4111-8111-111111111111',
    occurredAt: '2026-07-22T10:00:00+07:00',
    tripId: '22222222-2222-4222-8222-222222222222',
    operatorId: '33333333-3333-4333-8333-333333333333',
    loadedWeightKg: 80,
    maxCargoWeightKg: 100,
    percentFull: 80,
  };
}

function without<T extends object, TKey extends keyof T>(value: T, key: TKey): Omit<T, TKey> {
  const copy = { ...value };
  delete copy[key];
  return copy;
}
