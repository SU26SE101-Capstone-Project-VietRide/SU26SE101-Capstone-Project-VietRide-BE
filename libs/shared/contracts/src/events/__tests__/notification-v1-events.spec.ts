import {
  BOOKING_VOUCHER_CONSENT_REQUESTED_ROUTING_KEY,
  BookingVoucherConsentRequestedEventSchema,
  IDENTITY_OPERATOR_REGISTRATION_SUBMITTED_ROUTING_KEY,
  IDENTITY_SUBSCRIPTION_CUSTOM_REQUEST_APPROVED_ROUTING_KEY,
  IDENTITY_SUBSCRIPTION_CUSTOM_REQUEST_REJECTED_ROUTING_KEY,
  IDENTITY_SUBSCRIPTION_CUSTOM_REQUEST_SUBMITTED_ROUTING_KEY,
  IDENTITY_SUBSCRIPTION_USAGE_WARNING_ROUTING_KEY,
  IdentityOperatorRegistrationSubmittedEventSchema,
  IdentitySubscriptionCustomRequestApprovedEventSchema,
  IdentitySubscriptionCustomRequestRejectedEventSchema,
  IdentitySubscriptionCustomRequestSubmittedEventSchema,
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
    expect(IDENTITY_SUBSCRIPTION_CUSTOM_REQUEST_SUBMITTED_ROUTING_KEY).toBe(
      'identity.subscription_custom_request.submitted',
    );
    expect(IDENTITY_SUBSCRIPTION_CUSTOM_REQUEST_APPROVED_ROUTING_KEY).toBe(
      'identity.subscription_custom_request.approved',
    );
    expect(IDENTITY_SUBSCRIPTION_CUSTOM_REQUEST_REJECTED_ROUTING_KEY).toBe(
      'identity.subscription_custom_request.rejected',
    );
    expect(PAYMENT_WALLET_DEBITED_ROUTING_KEY).toBe('payment.wallet.debited');
    expect(BOOKING_VOUCHER_CONSENT_REQUESTED_ROUTING_KEY).toBe('booking.voucher.consent_requested');
  });

  it('accepts the three subscription custom request lifecycle facts', () => {
    const requestId = '91000000-0000-4000-8000-000000000008';
    expect(
      IdentitySubscriptionCustomRequestSubmittedEventSchema.safeParse({
        eventId,
        occurredAt,
        requestId,
        operatorId,
        operatorName: 'Nhà xe Việt Ride',
      }).success,
    ).toBe(true);
    expect(
      IdentitySubscriptionCustomRequestApprovedEventSchema.safeParse({
        eventId,
        occurredAt,
        requestId,
        operatorId,
        approvedPlanId: '91000000-0000-4000-8000-000000000009',
        planName: 'Doanh nghiệp riêng',
      }).success,
    ).toBe(true);
    expect(
      IdentitySubscriptionCustomRequestRejectedEventSchema.safeParse({
        eventId,
        occurredAt,
        requestId,
        operatorId,
        rejectionReason: 'Hạn mức chưa phù hợp.',
      }).success,
    ).toBe(true);
  });

  it.each([
    { eventId: 'not-a-uuid' },
    { occurredAt: '2026-07-27' },
    { requestId: undefined },
    { operatorId: 'not-a-uuid' },
    { operatorName: '   ' },
  ])('rejects malformed custom request submitted facts %#', (override) => {
    expect(
      IdentitySubscriptionCustomRequestSubmittedEventSchema.safeParse({
        eventId,
        occurredAt,
        requestId: '91000000-0000-4000-8000-000000000008',
        operatorId,
        operatorName: 'Nhà xe Việt Ride',
        ...override,
      }).success,
    ).toBe(false);
  });

  it('rejects approved and rejected facts with invalid required fields', () => {
    const common = {
      eventId,
      occurredAt,
      requestId: '91000000-0000-4000-8000-000000000008',
      operatorId,
    };
    expect(
      IdentitySubscriptionCustomRequestApprovedEventSchema.safeParse({
        ...common,
        approvedPlanId: 'not-a-uuid',
        planName: 'Doanh nghiệp riêng',
      }).success,
    ).toBe(false);
    expect(
      IdentitySubscriptionCustomRequestRejectedEventSchema.safeParse({
        ...common,
        rejectionReason: '   ',
      }).success,
    ).toBe(false);
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
