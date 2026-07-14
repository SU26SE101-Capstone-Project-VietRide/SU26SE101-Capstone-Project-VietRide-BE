import { z } from 'zod';

export const SubscriptionBuyerSnapshotSchema = z.object({
  name: z.string().trim().min(1),
  taxCode: z.string().trim().min(1).nullable(),
  address: z.string().trim().min(1).nullable(),
  email: z.string().email(),
});
export type SubscriptionBuyerSnapshot = z.infer<typeof SubscriptionBuyerSnapshotSchema>;

export const SubscriptionPaymentSucceededEventSchema = z.object({
  eventId: z.string().uuid(),
  occurredAt: z.string().datetime({ offset: true }),
  paymentId: z.string().uuid(),
  upgradeAttemptId: z.string().uuid(),
  operatorId: z.string().uuid(),
  operatorSubscriptionId: z.string().uuid(),
  amount: z.number().int().nonnegative(),
  method: z.enum(['VNPAY', 'WALLET']),
  planName: z.string().trim().min(1),
  billingPeriod: z.enum(['MONTHLY', 'YEARLY']),
  periodFrom: z.string().datetime({ offset: true }),
  periodTo: z.string().datetime({ offset: true }),
  buyerSnapshot: SubscriptionBuyerSnapshotSchema,
});
export type SubscriptionPaymentSucceededEvent = z.infer<
  typeof SubscriptionPaymentSucceededEventSchema
>;

export const SUBSCRIPTION_PAYMENT_SUCCEEDED_ROUTING_KEY = 'payment.subscription.payment_succeeded';
