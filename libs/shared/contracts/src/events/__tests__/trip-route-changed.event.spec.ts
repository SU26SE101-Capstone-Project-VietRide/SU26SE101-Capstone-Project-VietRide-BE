import { TripRouteChangedEventSchema } from '../trip-route-changed.event';

const payload = {
  eventId: '11111111-1111-4111-8111-111111111111',
  occurredAt: '2026-07-23T01:00:00Z',
  tripId: '22222222-2222-4222-8222-222222222222',
  operatorId: '33333333-3333-4333-8333-333333333333',
  tripStatus: 'IN_PROGRESS',
  alternativeRouteId: '44444444-4444-4444-8444-444444444444',
  affectedBookings: [
    {
      bookingId: '55555555-5555-4555-8555-555555555555',
      candidateStops: [
        {
          stopId: '66666666-6666-4666-8666-666666666666',
          stationId: null,
          stationName: 'Điểm dừng thay thế',
          sequence: 1,
          estimatedArrivalAt: '2026-07-23T01:45:00Z',
        },
        {
          stopId: null,
          stationId: '77777777-7777-4777-8777-777777777777',
          stationName: 'Bến đích',
          sequence: 2,
          estimatedArrivalAt: '2026-07-23T04:50:00Z',
        },
      ],
    },
  ],
} as const;

describe('TripRouteChangedEventSchema', () => {
  it('accepts the exact canonical immutable snapshot', () => {
    expect(TripRouteChangedEventSchema.safeParse(payload).success).toBe(true);
  });

  it('rejects the forbidden affectedBookingIds compatibility field', () => {
    expect(
      TripRouteChangedEventSchema.safeParse({ ...payload, affectedBookingIds: [] }).success,
    ).toBe(false);
  });

  it('rejects candidate stops without XOR identity', () => {
    expect(
      TripRouteChangedEventSchema.safeParse({
        ...payload,
        affectedBookings: [
          {
            ...payload.affectedBookings[0],
            candidateStops: [
              {
                ...payload.affectedBookings[0].candidateStops[0],
                stationId: '77777777-7777-4777-8777-777777777777',
              },
            ],
          },
        ],
      }).success,
    ).toBe(false);
  });
});
