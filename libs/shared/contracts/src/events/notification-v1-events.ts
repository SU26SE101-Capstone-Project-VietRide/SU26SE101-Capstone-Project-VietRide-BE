import { z } from 'zod';

const eventMetadataSchema = z.object({
  eventId: z.string().uuid(),
  occurredAt: z.string().datetime({ offset: true }),
});

export const IdentityOperatorRegistrationSubmittedEventSchema = eventMetadataSchema.extend({
  operatorId: z.string().uuid(),
  companyName: z.string().trim().min(1),
});
export type IdentityOperatorRegistrationSubmittedEvent = z.infer<
  typeof IdentityOperatorRegistrationSubmittedEventSchema
>;

export const IdentitySubscriptionUsageWarningEventSchema = eventMetadataSchema.extend({
  subscriptionId: z.string().uuid(),
  operatorId: z.string().uuid(),
  resource: z.string().trim().min(1),
  periodKey: z.string().trim().min(1),
  used: z.number().int().nonnegative(),
  limit: z.number().int().positive(),
  usagePercent: z.number().min(0).max(100),
});
export type IdentitySubscriptionUsageWarningEvent = z.infer<
  typeof IdentitySubscriptionUsageWarningEventSchema
>;

export const IdentitySubscriptionCustomRequestSubmittedEventSchema = eventMetadataSchema.extend({
  requestId: z.string().uuid(),
  operatorId: z.string().uuid(),
  operatorName: z.string().trim().min(1),
});
export type IdentitySubscriptionCustomRequestSubmittedEvent = z.infer<
  typeof IdentitySubscriptionCustomRequestSubmittedEventSchema
>;

export const IdentitySubscriptionCustomRequestApprovedEventSchema = eventMetadataSchema.extend({
  requestId: z.string().uuid(),
  operatorId: z.string().uuid(),
  approvedPlanId: z.string().uuid(),
  planName: z.string().trim().min(1),
});
export type IdentitySubscriptionCustomRequestApprovedEvent = z.infer<
  typeof IdentitySubscriptionCustomRequestApprovedEventSchema
>;

export const IdentitySubscriptionCustomRequestRejectedEventSchema = eventMetadataSchema.extend({
  requestId: z.string().uuid(),
  operatorId: z.string().uuid(),
  rejectionReason: z.string().trim().min(1),
});
export type IdentitySubscriptionCustomRequestRejectedEvent = z.infer<
  typeof IdentitySubscriptionCustomRequestRejectedEventSchema
>;

export const PaymentWalletDebitedEventSchema = eventMetadataSchema.extend({
  userId: z.string().uuid(),
  walletTransactionId: z.string().uuid().optional(),
  amount: z.number().int().positive(),
  balanceAfter: z.number().int().nonnegative().optional(),
  referenceType: z.string().trim().min(1),
  referenceId: z.string().uuid(),
});
export type PaymentWalletDebitedEvent = z.infer<typeof PaymentWalletDebitedEventSchema>;

export const BookingVoucherConsentRequestedEventSchema = eventMetadataSchema.extend({
  voucherId: z.string().uuid(),
  operatorId: z.string().uuid(),
  voucherCode: z.string().trim().min(1),
  voucherType: z.enum(['PERCENT_OFF', 'FIXED_AMOUNT']),
  voucherValue: z.number().int().positive(),
});
export type BookingVoucherConsentRequestedEvent = z.infer<
  typeof BookingVoucherConsentRequestedEventSchema
>;

export const IDENTITY_OPERATOR_REGISTRATION_SUBMITTED_ROUTING_KEY =
  'identity.operator.registration_submitted';
export const IDENTITY_SUBSCRIPTION_USAGE_WARNING_ROUTING_KEY =
  'identity.subscription.usage_warning';
export const IDENTITY_SUBSCRIPTION_CUSTOM_REQUEST_SUBMITTED_ROUTING_KEY =
  'identity.subscription_custom_request.submitted';
export const IDENTITY_SUBSCRIPTION_CUSTOM_REQUEST_APPROVED_ROUTING_KEY =
  'identity.subscription_custom_request.approved';
export const IDENTITY_SUBSCRIPTION_CUSTOM_REQUEST_REJECTED_ROUTING_KEY =
  'identity.subscription_custom_request.rejected';
export const PAYMENT_WALLET_DEBITED_ROUTING_KEY = 'payment.wallet.debited';
export const BOOKING_VOUCHER_CONSENT_REQUESTED_ROUTING_KEY = 'booking.voucher.consent_requested';
