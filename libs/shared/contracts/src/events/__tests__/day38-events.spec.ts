import {
  INVOICE_ISSUED_ROUTING_KEY,
  InvoiceIssuedEventSchema,
  PAYMENT_REFUNDED_ROUTING_KEY,
  PAYMENT_SUCCEEDED_ROUTING_KEY,
  PaymentRefundedEventSchema,
  PaymentSucceededEventSchema,
  SUBSCRIPTION_PAYMENT_SUCCEEDED_ROUTING_KEY,
  SubscriptionPaymentSucceededEventSchema,
  TRIP_COMPLETED_ROUTING_KEY,
  TRIP_DISRUPTED_ROUTING_KEY,
  TRIP_SETTLEMENT_COMPLETED_ROUTING_KEY,
  TripCompletedEventSchema,
  TripDisruptedEventSchema,
  TripSettlementCompletedEventSchema,
} from '../../index';

const eventId = '38000000-0000-0000-0000-000000000001';
const paymentId = '38000000-0000-0000-0000-000000000002';
const operatorId = '38000000-0000-0000-0000-000000000003';
const tripId = '38000000-0000-0000-0000-000000000004';
const referenceId = '38000000-0000-0000-0000-000000000005';
const occurredAt = '2026-07-15T10:00:00Z';

describe('Day 38 integration event contracts', () => {
  it('binds canonical routing keys', () => {
    expect(PAYMENT_SUCCEEDED_ROUTING_KEY).toBe('payment.payment.succeeded');
    expect(PAYMENT_REFUNDED_ROUTING_KEY).toBe('payment.payment.refunded');
    expect(SUBSCRIPTION_PAYMENT_SUCCEEDED_ROUTING_KEY).toBe(
      'payment.subscription.payment_succeeded',
    );
    expect(TRIP_COMPLETED_ROUTING_KEY).toBe('trip.trip.completed');
    expect(TRIP_DISRUPTED_ROUTING_KEY).toBe('trip.trip.disrupted');
    expect(INVOICE_ISSUED_ROUTING_KEY).toBe('payment.invoice.issued');
    expect(TRIP_SETTLEMENT_COMPLETED_ROUTING_KEY).toBe('payment.trip_settlement.completed');
  });

  it('accepts payment success and refund with trusted allocations', () => {
    const common = {
      eventId,
      occurredAt,
      paymentId,
      referenceType: 'BOOKING_GROUP',
      referenceId,
      amount: 300000,
      context: {
        version: 1,
        allocations: [
          {
            referenceId,
            referenceType: 'BOOKING',
            operatorId,
            tripId,
            grossAmount: 300000,
            voucherVietRideFundedAmount: 0,
            voucherOperatorFundedAmount: 0,
          },
        ],
      },
    } as const;

    expect(PaymentSucceededEventSchema.safeParse({ ...common, method: 'WALLET' }).success).toBe(
      true,
    );
    expect(PaymentRefundedEventSchema.safeParse(common).success).toBe(true);
  });

  it('rejects the obsolete trip completed payload', () => {
    expect(
      TripCompletedEventSchema.safeParse({
        eventId,
        occurredAt,
        tripId,
        fareVnd: 300000,
        driverId: operatorId,
        passengerId: referenceId,
      }).success,
    ).toBe(false);
  });

  it('accepts canonical completed and disrupted payloads', () => {
    const terminal = {
      eventId,
      occurredAt,
      tripId,
      operatorId,
      terminalAt: occurredAt,
      hasSubstitution: false,
    };
    expect(TripCompletedEventSchema.safeParse(terminal).success).toBe(true);
    expect(
      TripDisruptedEventSchema.safeParse({ ...terminal, reason: 'Mechanical issue' }).success,
    ).toBe(true);
  });

  it('accepts one canonical subscription success schema for both methods', () => {
    const payload = {
      eventId,
      occurredAt,
      paymentId,
      upgradeAttemptId: referenceId,
      operatorId,
      operatorSubscriptionId: '38000000-0000-0000-0000-000000000006',
      amount: 500000,
      method: 'VNPAY',
      planName: 'Pro',
      billingPeriod: 'MONTHLY',
      periodFrom: occurredAt,
      periodTo: '2026-08-15T10:00:00Z',
      buyerSnapshot: {
        name: 'VietRide Operator',
        taxCode: '0312345678',
        address: 'Ho Chi Minh City',
        email: 'operator@example.com',
      },
    };
    expect(SubscriptionPaymentSucceededEventSchema.safeParse(payload).success).toBe(true);
    expect(
      SubscriptionPaymentSucceededEventSchema.safeParse({ ...payload, method: 'WALLET' }).success,
    ).toBe(true);
  });

  it('keeps signed URLs out of invoice and settlement events', () => {
    expect(
      InvoiceIssuedEventSchema.safeParse({
        eventId,
        occurredAt,
        invoiceId: referenceId,
        invoiceNumber: 'VR-INV-202607-000001',
        operatorId,
        amount: 500000,
        invoiceWebUrl: `https://operator.vietride.vn/invoices/${referenceId}`,
        downloadApiUrl: `https://api.vietride.vn/v1/operator/invoices/${referenceId}/download`,
      }).success,
    ).toBe(true);

    expect(
      TripSettlementCompletedEventSchema.safeParse({
        eventId,
        occurredAt,
        settlementId: referenceId,
        tripId,
        operatorId,
        netAmount: 300000,
        settlementMethod: 'AUTO_WEEKLY',
        settledAt: occurredAt,
      }).success,
    ).toBe(true);
  });
});
