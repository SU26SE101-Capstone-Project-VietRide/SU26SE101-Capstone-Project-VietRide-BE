import {
  BOOKING_STOP_DISABLED_AFFECTED_ROUTING_KEY,
  BookingStopDisabledAffectedEventSchema,
} from '../../index';

const canonical = {
  eventId: '11111111-1111-4111-8111-111111111111',
  occurredAt: '2026-07-18T10:00:00+07:00',
  eventType: BOOKING_STOP_DISABLED_AFFECTED_ROUTING_KEY,
  stopId: '22222222-2222-4222-8222-222222222222',
  replacedByStopId: '33333333-3333-4333-8333-333333333333',
  recipientUserIds: [
    '44444444-4444-4444-8444-444444444444',
    '55555555-5555-4555-8555-555555555555',
  ],
  affectedBookingCount: 2,
};

describe('Day 24 booking stop-disabled affected contract:', () => {
  it('accepts the exact D24-6 payload and optional replacement omission', () => {
    expect(BookingStopDisabledAffectedEventSchema.parse(canonical)).toEqual(canonical);
    const { replacedByStopId, ...withoutReplacement } = canonical;
    void replacedByStopId;
    expect(BookingStopDisabledAffectedEventSchema.parse(withoutReplacement)).toEqual(
      withoutReplacement,
    );
  });

  it('rejects wrong keys, extra fields, empty recipients, duplicates, and invalid counts', () => {
    expect(
      BookingStopDisabledAffectedEventSchema.safeParse({
        ...canonical,
        eventType: 'trip.stop.disabled',
      }).success,
    ).toBe(false);
    expect(
      BookingStopDisabledAffectedEventSchema.safeParse({
        ...canonical,
        operatorId: canonical.stopId,
      }).success,
    ).toBe(false);
    expect(
      BookingStopDisabledAffectedEventSchema.safeParse({ ...canonical, recipientUserIds: [] })
        .success,
    ).toBe(false);
    expect(
      BookingStopDisabledAffectedEventSchema.safeParse({
        ...canonical,
        recipientUserIds: [canonical.recipientUserIds[0], canonical.recipientUserIds[0]],
      }).success,
    ).toBe(false);
    expect(
      BookingStopDisabledAffectedEventSchema.safeParse({ ...canonical, affectedBookingCount: 0 })
        .success,
    ).toBe(false);
  });
});
