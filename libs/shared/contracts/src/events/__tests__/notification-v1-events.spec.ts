import {
  BOOKING_VOUCHER_CONSENT_REQUESTED_ROUTING_KEY,
  BookingVoucherConsentRequestedEventSchema,
  IDENTITY_OPERATOR_REGISTRATION_SUBMITTED_ROUTING_KEY,
  IDENTITY_SUBSCRIPTION_USAGE_WARNING_ROUTING_KEY,
  IdentityOperatorRegistrationSubmittedEventSchema,
  IdentitySubscriptionUsageWarningEventSchema,
  PAYMENT_WALLET_DEBITED_ROUTING_KEY,
  PaymentWalletDebitedEventSchema,
} from '../../index';

const eventId = '91000000-0000-4000-8000-000000000001';
const occurredAt = '2026-07-27T08:00:00+07:00';
const operatorId = '91000000-0000-4000-8000-000000000002';

describe('Notification v1 integration event contracts', () => {
  it('binds the canonical routing keys', () => {
    expect(IDENTITY_OPERATOR_REGISTRATION_SUBMITTED_ROUTING_KEY).toBe(
      'identity.operator.registration_submitted',
    );
    expect(IDENTITY_SUBSCRIPTION_USAGE_WARNING_ROUTING_KEY).toBe(
      'identity.subscription.usage_warning',
    );
    expect(PAYMENT_WALLET_DEBITED_ROUTING_KEY).toBe('payment.wallet.debited');
    expect(BOOKING_VOUCHER_CONSENT_REQUESTED_ROUTING_KEY).toBe(
      'booking.voucher.consent_requested',
    );
  });

  it('accepts operator registration and subscription usage warning facts', () => {
    expect(
      IdentityOperatorRegistrationSubmittedEventSchema.safeParse({
        eventId,
        occurredAt,
        operatorId,
        companyName: 'Nhà xe Việt Ride',
      }).success,
    ).toBe(true);
    expect(
      IdentitySubscriptionUsageWarningEventSchema.safeParse({
        eventId,
        occurredAt,
        subscriptionId: '91000000-0000-4000-8000-000000000003',
        operatorId,
        resource: 'TRIPS',
        periodKey: '2026-07',
        used: 8,
        limit: 10,
        usagePercent: 80,
      }).success,
    ).toBe(true);
  });

  it('accepts wallet debit and voucher consent requested facts', () => {
    expect(
      PaymentWalletDebitedEventSchema.safeParse({
        eventId,
        occurredAt,
        userId: '91000000-0000-4000-8000-000000000004',
        walletTransactionId: '91000000-0000-4000-8000-000000000005',
        amount: 150000,
        balanceAfter: 850000,
        referenceType: 'BOOKING_PAYMENT',
        referenceId: '91000000-0000-4000-8000-000000000006',
      }).success,
    ).toBe(true);
    expect(
      BookingVoucherConsentRequestedEventSchema.safeParse({
        eventId,
        occurredAt,
        voucherId: '91000000-0000-4000-8000-000000000007',
        operatorId,
        voucherCode: 'TET2026',
        voucherType: 'PERCENT_OFF',
        voucherValue: 10,
      }).success,
    ).toBe(true);
  });

  it('rejects an invalid percentage and a debit without a positive amount', () => {
    expect(
      IdentitySubscriptionUsageWarningEventSchema.safeParse({
        eventId,
        occurredAt,
        subscriptionId: '91000000-0000-4000-8000-000000000003',
        operatorId,
        resource: 'TRIPS',
        periodKey: '2026-07',
        used: 8,
        limit: 10,
        usagePercent: 101,
      }).success,
    ).toBe(false);
    expect(
      PaymentWalletDebitedEventSchema.safeParse({
        eventId,
        occurredAt,
        userId: '91000000-0000-4000-8000-000000000004',
        amount: 0,
        referenceType: 'BOOKING_PAYMENT',
        referenceId: '91000000-0000-4000-8000-000000000006',
      }).success,
    ).toBe(false);
  });
});
