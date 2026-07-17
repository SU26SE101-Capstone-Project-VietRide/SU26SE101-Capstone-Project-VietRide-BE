import {
  BOOKING_PENDING_ACTION_AUTO_RESOLVED_ROUTING_KEY,
  BookingCancelledConsumerEventSchema,
  BookingCancelledEventSchema,
  BookingPendingActionAutoResolvedEventSchema,
} from '../../index';

const canonicalAutoResolved = {
  eventId: '11111111-1111-4111-8111-111111111111',
  occurredAt: '2026-07-17T10:00:01+07:00',
  bookingId: '22222222-2222-4222-8222-222222222222',
  tripId: '33333333-3333-4333-8333-333333333333',
  userId: '44444444-4444-4444-8444-444444444444',
  pendingActionId: '55555555-5555-4555-8555-555555555555',
  resolvedAction: 'ACCEPTED' as const,
  severity: 'MAJOR' as const,
  oldDeparture: '2026-07-18T01:00:00+07:00',
  newDeparture: '2026-07-18T08:00:00+07:00',
};

const cancellationFields = {
  bookingId: canonicalAutoResolved.bookingId,
  userId: canonicalAutoResolved.userId,
  refundAmount: 100_000,
  refundOverride: true,
  cancellationReason: 'SCHEDULE_CHANGED',
};

describe('Day 23 schedule-change contract:', () => {
  it('exports the exact strict pending-action auto-resolved contract', () => {
    expect(BOOKING_PENDING_ACTION_AUTO_RESOLVED_ROUTING_KEY).toBe(
      'booking.booking.pending_action_auto_resolved',
    );
    expect(BookingPendingActionAutoResolvedEventSchema.parse(canonicalAutoResolved)).toEqual(
      canonicalAutoResolved,
    );

    expect(
      BookingPendingActionAutoResolvedEventSchema.safeParse({
        ...canonicalAutoResolved,
        resolvedAction: 'REJECTED',
      }).success,
    ).toBe(false);
    expect(
      BookingPendingActionAutoResolvedEventSchema.safeParse({
        ...canonicalAutoResolved,
        severity: 'MINOR',
      }).success,
    ).toBe(false);
    expect(
      BookingPendingActionAutoResolvedEventSchema.safeParse({
        ...canonicalAutoResolved,
        deadline: canonicalAutoResolved.newDeparture,
      }).success,
    ).toBe(false);
  });

  it('preserves the exported Task 23.5 canonical and one-release cancellation schemas', () => {
    const canonicalCancellation = {
      eventId: '66666666-6666-4666-8666-666666666666',
      occurredAt: '2026-07-17T10:00:00+07:00',
      ...cancellationFields,
    };

    expect(BookingCancelledEventSchema.parse(canonicalCancellation)).toEqual(
      canonicalCancellation,
    );
    expect(BookingCancelledConsumerEventSchema.parse(canonicalCancellation)).toEqual(
      canonicalCancellation,
    );
    expect(BookingCancelledConsumerEventSchema.parse(cancellationFields)).toEqual(
      cancellationFields,
    );
    expect(BookingCancelledEventSchema.safeParse(cancellationFields).success).toBe(false);
    expect(
      BookingCancelledConsumerEventSchema.safeParse({
        ...cancellationFields,
        eventId: canonicalCancellation.eventId,
      }).success,
    ).toBe(false);
  });
});
