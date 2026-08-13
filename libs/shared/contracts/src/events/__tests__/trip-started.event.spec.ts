import { TRIP_STARTED_ROUTING_KEY, TripStartedEventSchema } from '../../index';

describe('TripStartedEventSchema', () => {
  const payload = {
    tripId: '11111111-1111-4111-8111-111111111111',
    actualDepartureTime: '2026-08-11T03:00:00+00:00',
  };

  it('accepts the canonical Trip Outbox payload', () => {
    expect(TripStartedEventSchema.parse(payload)).toEqual(payload);
    expect(TRIP_STARTED_ROUTING_KEY).toBe('trip.trip.started');
  });

  it('rejects malformed or expanded payloads', () => {
    expect(TripStartedEventSchema.safeParse({ ...payload, tripId: 'invalid' }).success).toBe(false);
    expect(TripStartedEventSchema.safeParse({ ...payload, unexpected: true }).success).toBe(false);
  });
});
