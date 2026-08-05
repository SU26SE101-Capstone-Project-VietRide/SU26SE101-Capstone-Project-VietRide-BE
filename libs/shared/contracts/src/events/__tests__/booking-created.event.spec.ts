import {
  BookingCreatedEventSchema,
  OperationalBookingCreatedEventSchema,
} from '../../index';

const baseEvent = {
  eventId: '55555555-5555-4555-8555-555555555555',
  occurredAt: '2026-08-05T01:00:00.000Z',
  bookingId: '33333333-3333-4333-8333-333333333333',
};

const operationalEvent = {
  ...baseEvent,
  bookingCode: 'VR-20260805-ABCDEFGH',
  tripId: '44444444-4444-4444-8444-444444444444',
  status: 'CONFIRMED',
  ticketCodes: ['VT-20260805-ABCDEFGH'],
  passengerCount: 1,
  pickup: {
    stationId: '66666666-6666-4666-8666-666666666666',
    stopId: null,
    address: null,
  },
  dropoff: {
    stationId: null,
    stopId: '77777777-7777-4777-8777-777777777777',
    address: null,
  },
  driverUserId: '11111111-1111-4111-8111-111111111111',
  assistantUserId: null,
};

describe('BookingCreatedEventSchema', () => {
  it('keeps accepting the legacy contract while accepting the operational crew contract', () => {
    expect(
      BookingCreatedEventSchema.safeParse({
        ...baseEvent,
        passengerId: '22222222-2222-4222-8222-222222222222',
        pickupLocation: { lat: 10.77, lng: 106.7 },
        dropoffLocation: { lat: 10.78, lng: 106.71 },
      }).success,
    ).toBe(true);
    expect(BookingCreatedEventSchema.safeParse(operationalEvent).success).toBe(true);
  });

  it('requires a driver, at least one ticket, and a matching passenger count', () => {
    expect(
      OperationalBookingCreatedEventSchema.safeParse({
        ...operationalEvent,
        driverUserId: null,
      }).success,
    ).toBe(false);
    expect(
      OperationalBookingCreatedEventSchema.safeParse({
        ...operationalEvent,
        ticketCodes: [],
        passengerCount: 1,
      }).success,
    ).toBe(false);
    expect(
      OperationalBookingCreatedEventSchema.safeParse({
        ...operationalEvent,
        passengerCount: 2,
      }).success,
    ).toBe(false);
  });
});
