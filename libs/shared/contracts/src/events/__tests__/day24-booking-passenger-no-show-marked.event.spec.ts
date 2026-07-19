import {
  BOOKING_PASSENGER_NO_SHOW_MARKED_ROUTING_KEY,
  BookingPassengerNoShowMarkedEventSchema,
} from '../../index';

const canonical = {
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

describe('Day 24 passenger no-show event contract:', () => {
  it('accepts along-route and terminal shapes with the frozen discriminants', () => {
    expect(BookingPassengerNoShowMarkedEventSchema.parse(canonical)).toEqual(canonical);
    const terminal = {
      ...canonical,
      bookingStatus: 'NO_SHOW' as const,
      triggerType: 'TERMINAL' as const,
    };
    delete (terminal as Partial<typeof terminal>).pickupStopId;
    expect(BookingPassengerNoShowMarkedEventSchema.parse(terminal)).toEqual(terminal);
  });

  it('rejects wrong anchors, empty passenger ids, extra fields, and unsupported statuses', () => {
    expect(
      BookingPassengerNoShowMarkedEventSchema.safeParse({
        ...canonical,
        triggerType: 'TERMINAL',
      }).success,
    ).toBe(false);
    const { pickupStopId, ...missingAlongRouteAnchor } = canonical;
    void pickupStopId;
    expect(BookingPassengerNoShowMarkedEventSchema.safeParse(missingAlongRouteAnchor).success).toBe(
      false,
    );
    expect(
      BookingPassengerNoShowMarkedEventSchema.safeParse({
        ...canonical,
        newlyNoShowPassengerIds: [],
      }).success,
    ).toBe(false);
    expect(
      BookingPassengerNoShowMarkedEventSchema.safeParse({
        ...canonical,
        bookingStatus: 'CONFIRMED',
      }).success,
    ).toBe(false);
    expect(
      BookingPassengerNoShowMarkedEventSchema.safeParse({ ...canonical, refundAmount: 0 }).success,
    ).toBe(false);
  });
});
