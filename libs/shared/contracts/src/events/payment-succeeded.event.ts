import { z } from 'zod';

export const PaymentMethodSchema = z.enum(['vnpay', 'wallet', 'cash', 'card']);
export type PaymentMethod = z.infer<typeof PaymentMethodSchema>;

export const PaymentSucceededEventSchema = z.object({
  eventId: z.string().uuid(),
  occurredAt: z.string().datetime({ offset: true }),
  paymentId: z.string().uuid(),
  bookingId: z.string().uuid(),
  amountVnd: z.number().int().nonnegative(),
  method: PaymentMethodSchema,
});

export type PaymentSucceededEvent = z.infer<typeof PaymentSucceededEventSchema>;

export const PAYMENT_SUCCEEDED_ROUTING_KEY = 'payment.succeeded';
