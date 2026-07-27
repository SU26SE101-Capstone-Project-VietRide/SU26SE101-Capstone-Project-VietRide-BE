import { z } from 'zod';

const eventIdentityFields = {
  eventId: z.string().uuid(),
  occurredAt: z.string().datetime({ offset: true }),
};

const parcelLoadedEventSchema = z
  .object({
    ...eventIdentityFields,
    parcelId: z.string().uuid(),
    tripId: z.string().uuid(),
    actualWeightKg: z.number().positive(),
    userIds: z.array(z.string().uuid()).min(1),
  })
  .strict();

export type ParcelLoadedEvent = z.infer<typeof parcelLoadedEventSchema>;

const parcelAutoRejectedLegacyEventSchema = z
  .object({
    ...eventIdentityFields,
    parcelId: z.string().uuid(),
    parcelCode: z.string().trim().min(1),
    operatorId: z.string().uuid(),
    userId: z.string().uuid(),
    tripId: z.string().uuid(),
    refundAmount: z.number().int().nonnegative(),
  })
  .strict();

const parcelAutoRejectedSettlementV2EventSchema = z
  .object({
    ...eventIdentityFields,
    parcelId: z.string().uuid(),
    parcelCode: z.string().trim().min(1),
    operatorId: z.string().uuid(),
    userId: z.string().uuid(),
    tripId: z.string().uuid(),
    reason: z.enum(['CHECK_IN_TIMEOUT', 'FINAL_PAYMENT_TIMEOUT']),
    forfeitedDepositVnd: z.number().int().nonnegative(),
    refundAmount: z.number().int().nonnegative(),
  })
  .strict();

const parcelAutoRejectedEventSchema = z.union([
  parcelAutoRejectedLegacyEventSchema,
  parcelAutoRejectedSettlementV2EventSchema,
]);

const parcelReviewApprovedEventSchema = z
  .object({
    ...eventIdentityFields,
    parcelId: z.string().uuid(),
    parcelCode: z.string().trim().min(1),
    operatorId: z.string().uuid(),
    userId: z.string().uuid(),
    depositRequiredVnd: z.number().int().nonnegative(),
  })
  .strict();

const parcelFinalPaymentRequestedEventSchema = z
  .object({
    ...eventIdentityFields,
    parcelId: z.string().uuid(),
    parcelCode: z.string().trim().min(1),
    operatorId: z.string().uuid(),
    userId: z.string().uuid(),
    tripId: z.string().uuid(),
    balanceRequiredVnd: z.number().int().nonnegative(),
    balancePaidVnd: z.number().int().nonnegative(),
    finalPaymentDeadline: z.string().datetime({ offset: true }),
  })
  .strict();

const parcelSettlementRecoveredEventSchema = z
  .object({
    ...eventIdentityFields,
    parcelId: z.string().uuid(),
    parcelCode: z.string().trim().min(1),
    userId: z.string().uuid(),
    tripId: z.string().uuid(),
    recoveredStatus: z.enum(['READY_TO_LOAD', 'CANCELLED']),
    refundAmountVnd: z.number().int().nonnegative(),
  })
  .strict();

export type ParcelAutoRejectedEvent = z.infer<typeof parcelAutoRejectedEventSchema>;
export type ParcelReviewApprovedEvent = z.infer<typeof parcelReviewApprovedEventSchema>;
export type ParcelFinalPaymentRequestedEvent = z.infer<
  typeof parcelFinalPaymentRequestedEventSchema
>;
export type ParcelSettlementRecoveredEvent = z.infer<
  typeof parcelSettlementRecoveredEventSchema
>;

export {
  parcelLoadedEventSchema as ParcelLoadedEventSchema,
  parcelAutoRejectedEventSchema as ParcelAutoRejectedEventSchema,
  parcelReviewApprovedEventSchema as ParcelReviewApprovedEventSchema,
  parcelFinalPaymentRequestedEventSchema as ParcelFinalPaymentRequestedEventSchema,
  parcelSettlementRecoveredEventSchema as ParcelSettlementRecoveredEventSchema,
};

export const PARCEL_LOADED_ROUTING_KEY = 'parcel.parcel.loaded';
export const PARCEL_AUTO_REJECTED_ROUTING_KEY = 'parcel.parcel.auto_rejected';
export const PARCEL_REVIEW_APPROVED_ROUTING_KEY = 'parcel.parcel.review_approved';
export const PARCEL_FINAL_PAYMENT_REQUESTED_ROUTING_KEY =
  'parcel.parcel.final_payment_requested';
export const PARCEL_SETTLEMENT_RECOVERED_ROUTING_KEY = 'parcel.parcel.settlement_recovered';
