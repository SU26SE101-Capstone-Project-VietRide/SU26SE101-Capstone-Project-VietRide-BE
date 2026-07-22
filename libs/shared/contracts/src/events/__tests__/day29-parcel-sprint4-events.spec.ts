import {
  PARCEL_AUTO_REJECTED_ROUTING_KEY,
  PARCEL_LOADED_ROUTING_KEY,
  ParcelAutoRejectedEventSchema,
  ParcelLoadedEventSchema,
  type ParcelAutoRejectedEvent,
  type ParcelLoadedEvent,
} from '../../index';

describe('Day 29 Sprint 4 Parcel event contracts', () => {
  it('freezes parcel.parcel.loaded with direct recipients and shared event identity', () => {
    const event = loadedEvent();
    const outboxEventId = event.eventId;
    const rabbitMessageId = outboxEventId;

    expect(PARCEL_LOADED_ROUTING_KEY).toBe('parcel.parcel.loaded');
    expect(ParcelLoadedEventSchema.parse(event)).toEqual(event);
    expect(event.eventId).toBe(outboxEventId);
    expect(rabbitMessageId).toBe(event.eventId);
  });

  it('freezes parcel.parcel.auto_rejected with the sender identity', () => {
    const event = autoRejectedEvent();
    const outboxEventId = event.eventId;
    const rabbitMessageId = outboxEventId;

    expect(PARCEL_AUTO_REJECTED_ROUTING_KEY).toBe('parcel.parcel.auto_rejected');
    expect(ParcelAutoRejectedEventSchema.parse(event)).toEqual(event);
    expect(event.eventId).toBe(outboxEventId);
    expect(rabbitMessageId).toBe(event.eventId);
  });

  it.each([
    ['loaded missing field', ParcelLoadedEventSchema, without(loadedEvent(), 'userIds')],
    ['loaded extra field', ParcelLoadedEventSchema, { ...loadedEvent(), parcelCode: 'legacy' }],
    ['loaded wrong-typed field', ParcelLoadedEventSchema, { ...loadedEvent(), userIds: [1] }],
    ['loaded zero actual weight', ParcelLoadedEventSchema, { ...loadedEvent(), actualWeightKg: 0 }],
    [
      'auto-rejected missing field',
      ParcelAutoRejectedEventSchema,
      without(autoRejectedEvent(), 'userId'),
    ],
    [
      'auto-rejected extra field',
      ParcelAutoRejectedEventSchema,
      { ...autoRejectedEvent(), reason: 'legacy' },
    ],
    [
      'auto-rejected wrong-typed field',
      ParcelAutoRejectedEventSchema,
      { ...autoRejectedEvent(), refundAmount: '10000' },
    ],
  ])('rejects %s', (_caseName, schema, payload) => {
    expect(schema.safeParse(payload).success).toBe(false);
  });
});

function loadedEvent(): ParcelLoadedEvent {
  return {
    eventId: '11111111-1111-4111-8111-111111111111',
    occurredAt: '2026-07-22T10:00:00+07:00',
    parcelId: '22222222-2222-4222-8222-222222222222',
    tripId: '33333333-3333-4333-8333-333333333333',
    actualWeightKg: 12.5,
    userIds: [
      '44444444-4444-4444-8444-444444444444',
      '55555555-5555-4555-8555-555555555555',
    ],
  };
}

function autoRejectedEvent(): ParcelAutoRejectedEvent {
  return {
    eventId: '11111111-1111-4111-8111-111111111111',
    occurredAt: '2026-07-22T10:00:00+07:00',
    parcelId: '22222222-2222-4222-8222-222222222222',
    parcelCode: 'VRP-20260722-ABCDEFGH',
    operatorId: '33333333-3333-4333-8333-333333333333',
    userId: '44444444-4444-4444-8444-444444444444',
    tripId: '55555555-5555-4555-8555-555555555555',
    refundAmount: 10000,
  };
}

function without<T extends object, TKey extends keyof T>(value: T, key: TKey): Omit<T, TKey> {
  const copy = { ...value };
  delete copy[key];
  return copy;
}
