import { z } from 'zod';

export const PaymentMethodSchema = z.enum(['VNPAY', 'WALLET']);
export type PaymentMethod = z.infer<typeof PaymentMethodSchema>;

export const PaymentReferenceTypeSchema = z.enum([
  'BOOKING',
  'BOOKING_GROUP',
  'PARCEL',
  'PARCEL_ADDITIONAL',
  'SUBSCRIPTION',
]);
export type PaymentReferenceType = z.infer<typeof PaymentReferenceTypeSchema>;

export const PaymentAllocationSchema = z.object({
  referenceId: z.string().uuid(),
  referenceType: z.enum(['BOOKING', 'PARCEL', 'PARCEL_ADDITIONAL']),
  operatorId: z.string().uuid(),
  tripId: z.string().uuid(),
  grossAmount: z.number().int().nonnegative(),
  voucherVietRideFundedAmount: z.number().int().nonnegative().default(0),
  voucherOperatorFundedAmount: z.number().int().nonnegative().default(0),
});
export type PaymentAllocation = z.infer<typeof PaymentAllocationSchema>;

export const PaymentContextSchema = z.object({
  version: z.literal(1),
  allocations: z.array(PaymentAllocationSchema).min(1),
});
export type PaymentContext = z.infer<typeof PaymentContextSchema>;

export const PaymentSucceededEventSchema = z.object({
  eventId: z.string().uuid(),
  occurredAt: z.string().datetime({ offset: true }),
  paymentId: z.string().uuid(),
  referenceType: PaymentReferenceTypeSchema,
  referenceId: z.string().uuid(),
  amount: z.number().int().nonnegative(),
  method: PaymentMethodSchema,
  context: PaymentContextSchema,
});

export type PaymentSucceededEvent = z.infer<typeof PaymentSucceededEventSchema>;

export const PaymentRefundedEventSchema = z.object({
  eventId: z.string().uuid(),
  occurredAt: z.string().datetime({ offset: true }),
  paymentId: z.string().uuid(),
  referenceType: PaymentReferenceTypeSchema,
  referenceId: z.string().uuid(),
  amount: z.number().int().positive(),
  context: PaymentContextSchema,
});
export type PaymentRefundedEvent = z.infer<typeof PaymentRefundedEventSchema>;

// <service>.<aggregate>.<verb_past> per BACKEND_SOURCE_OF_TRUTH §7.3.
export const PAYMENT_SUCCEEDED_ROUTING_KEY = 'payment.payment.succeeded';
export const PAYMENT_REFUNDED_ROUTING_KEY = 'payment.payment.refunded';
