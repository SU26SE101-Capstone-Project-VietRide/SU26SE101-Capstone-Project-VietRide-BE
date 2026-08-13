import {
  PARCEL_AUTO_REJECTED_ROUTING_KEY,
  PARCEL_FINAL_PAYMENT_REQUESTED_ROUTING_KEY,
  PARCEL_LOADED_ROUTING_KEY,
  PARCEL_REVIEW_APPROVED_ROUTING_KEY,
  PARCEL_RESERVED_ROUTING_KEY,
  PARCEL_SETTLEMENT_RECOVERED_ROUTING_KEY,
  ParcelAutoRejectedEventSchema,
  ParcelFinalPaymentRequestedEventSchema,
  ParcelLoadedEventSchema,
  ParcelReviewApprovedEventSchema,
  ParcelReservedEventSchema,
  ParcelSettlementRecoveredEventSchema,
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

  it('freezes parcel.parcel.reserved for Assistant notification fan-out', () => {
    const event = {
      eventId: '11111111-1111-4111-8111-111111111111',
      occurredAt: '2026-08-13T10:00:00+07:00',
      parcelId: '22222222-2222-4222-8222-222222222222',
      parcelCode: 'VRP-20260813-ABCDEFGH',
      tripId: '33333333-3333-4333-8333-333333333333',
      operatorId: '44444444-4444-4444-8444-444444444444',
      senderUserId: '55555555-5555-4555-8555-555555555555',
    };

    expect(PARCEL_RESERVED_ROUTING_KEY).toBe('parcel.parcel.reserved');
    expect(ParcelReservedEventSchema.parse(event)).toEqual(event);
    expect(ParcelReservedEventSchema.safeParse({ ...event, driverUserId: event.senderUserId }).success)
      .toBe(false);
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

  it('accepts the settlement-v2 auto-rejected payload', () => {
    const event = {
      ...autoRejectedEvent(),
      reason: 'FINAL_PAYMENT_TIMEOUT',
      forfeitedDepositVnd: 20000,
      refundAmount: 0,
    };

    expect(ParcelAutoRejectedEventSchema.parse(event)).toEqual(event);
  });

  it('freezes the passenger-facing parcel settlement events', () => {
    const reviewApproved = {
      eventId: '11111111-1111-4111-8111-111111111111',
      occurredAt: '2026-07-27T10:00:00+07:00',
      parcelId: '22222222-2222-4222-8222-222222222222',
      parcelCode: 'VRP-20260727-ABCDEFGH',
      operatorId: '33333333-3333-4333-8333-333333333333',
      userId: '44444444-4444-4444-8444-444444444444',
      depositRequiredVnd: 30000,
    };
    const finalPaymentRequested = {
      eventId: '11111111-1111-4111-8111-111111111112',
      occurredAt: '2026-07-27T10:05:00+07:00',
      parcelId: reviewApproved.parcelId,
      parcelCode: reviewApproved.parcelCode,
      operatorId: reviewApproved.operatorId,
      userId: reviewApproved.userId,
      tripId: '55555555-5555-4555-8555-555555555555',
      balanceRequiredVnd: 120000,
      balancePaidVnd: 0,
      finalPaymentDeadline: '2026-07-27T10:35:00+07:00',
    };
    const settlementRecovered = {
      eventId: '11111111-1111-4111-8111-111111111113',
      occurredAt: '2026-07-27T10:10:00+07:00',
      parcelId: reviewApproved.parcelId,
      parcelCode: reviewApproved.parcelCode,
      userId: reviewApproved.userId,
      tripId: finalPaymentRequested.tripId,
      recoveredStatus: 'READY_TO_LOAD',
      refundAmountVnd: 0,
    };

    expect(PARCEL_REVIEW_APPROVED_ROUTING_KEY).toBe('parcel.parcel.review_approved');
    expect(ParcelReviewApprovedEventSchema.parse(reviewApproved)).toEqual(reviewApproved);
    expect(PARCEL_FINAL_PAYMENT_REQUESTED_ROUTING_KEY).toBe(
      'parcel.parcel.final_payment_requested',
    );
    expect(ParcelFinalPaymentRequestedEventSchema.parse(finalPaymentRequested)).toEqual(
      finalPaymentRequested,
    );
    expect(PARCEL_SETTLEMENT_RECOVERED_ROUTING_KEY).toBe(
      'parcel.parcel.settlement_recovered',
    );
    expect(ParcelSettlementRecoveredEventSchema.parse(settlementRecovered)).toEqual(
      settlementRecovered,
    );
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
      'auto-rejected invalid settlement reason',
      ParcelAutoRejectedEventSchema,
      {
        ...autoRejectedEvent(),
        reason: 'LEGACY',
        forfeitedDepositVnd: 10000,
      },
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
