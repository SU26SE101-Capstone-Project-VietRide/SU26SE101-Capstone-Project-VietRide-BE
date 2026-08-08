import {
  BookingCancelledConsumerEventSchema,
  BookingCancelledEventSchema,
  BookingCancelledLegacyEventSchema,
  OperationalBookingCancelledEventSchema,
  BOOKING_CANCELLED_ROUTING_KEY,
} from '../..';

const canonical = {
  eventId: '11111111-1111-4111-8111-111111111111',
  occurredAt: '2026-07-17T00:00:00+00:00',
  bookingId: '22222222-2222-4222-8222-222222222222',
  userId: '33333333-3333-4333-8333-333333333333',
  refundAmount: 120000,
  refundOverride: false,
  cancellationReason: 'USER_INITIATED',
};

const operational = {
  ...canonical,
  bookingCode: 'VR-20260717-ABCDEFGH',
  ticketCodes: ['VT-20260717-ABCDEFGH'],
  ticketCount: 1,
  tripId: '44444444-4444-4444-8444-444444444444',
  previousStatus: 'CONFIRMED',
  seatNumbers: ['A01'],
};

describe('Day 23 booking.cancelled contract:', () => {
  it('accepts only the complete canonical producer shape', () => {
    expect(BOOKING_CANCELLED_ROUTING_KEY).toBe('booking.booking.cancelled');
    expect(BookingCancelledEventSchema.safeParse(canonical).success).toBe(true);
    expect(BookingCancelledEventSchema.safeParse({ ...canonical, extra: true }).success).toBe(false);
    expect(BookingCancelledEventSchema.safeParse({ ...canonical, eventId: 'not-a-uuid' }).success).toBe(false);
    expect(BookingCancelledEventSchema.safeParse({ ...canonical, occurredAt: '2026-07-17' }).success).toBe(false);
    expect(BookingCancelledEventSchema.safeParse({ ...canonical, refundAmount: -1 }).success).toBe(false);
    expect(BookingCancelledEventSchema.safeParse({ ...canonical, refundAmount: '12.5' }).success).toBe(false);
    expect(BookingCancelledEventSchema.safeParse({ ...canonical, cancellationReason: ' ' }).success).toBe(false);
    expect(BookingCancelledEventSchema.safeParse({ ...canonical, ticketCodes: [''] }).success).toBe(false);
    const { eventId, ...withoutIdentity } = canonical;
    expect(eventId).toBe(canonical.eventId);
    expect(BookingCancelledEventSchema.safeParse(withoutIdentity).success).toBe(false);
  });

  it('accepts the operational manifest-change shape without weakening legacy parsing', () => {
    expect(OperationalBookingCancelledEventSchema.safeParse(operational).success).toBe(true);
    expect(BookingCancelledConsumerEventSchema.safeParse(operational).success).toBe(true);
    expect(
      OperationalBookingCancelledEventSchema.safeParse({ ...operational, tripId: undefined }).success,
    ).toBe(false);
    expect(
      OperationalBookingCancelledEventSchema.safeParse({ ...operational, previousStatus: 'CANCELLED' }).success,
    ).toBe(false);
    expect(
      OperationalBookingCancelledEventSchema.safeParse({ ...operational, seatNumbers: [] }).success,
    ).toBe(false);
  });

  it('permits only the exact legacy identity omission for consumers', () => {
    const { eventId, occurredAt, ...legacy } = canonical;
    expect(eventId).toBe(canonical.eventId);
    expect(occurredAt).toBe(canonical.occurredAt);
    expect(BookingCancelledLegacyEventSchema.safeParse(legacy).success).toBe(true);
    expect(BookingCancelledConsumerEventSchema.safeParse(canonical).success).toBe(true);
    expect(BookingCancelledConsumerEventSchema.safeParse(legacy).success).toBe(true);
    expect(BookingCancelledConsumerEventSchema.safeParse({ ...legacy, eventId: canonical.eventId }).success).toBe(false);
    expect(BookingCancelledConsumerEventSchema.safeParse({ ...legacy, occurredAt: canonical.occurredAt }).success).toBe(false);
    expect(BookingCancelledConsumerEventSchema.safeParse({ ...canonical, eventId: 'bad' }).success).toBe(false);
    expect(BookingCancelledConsumerEventSchema.safeParse({ ...legacy, extra: true }).success).toBe(false);
  });
});
