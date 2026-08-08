import { z } from 'zod';

const moneyAmountSchema = z.union([z.number().int().nonnegative(), z.string().regex(/^\d+$/)]);

const bookingCancelledFields = {
  bookingId: z.string().uuid(),
  userId: z.string().uuid(),
  refundAmount: moneyAmountSchema,
  refundOverride: z.boolean(),
  cancellationReason: z.string().trim().min(1),
  bookingCode: z.string().trim().min(1).optional(),
  ticketCodes: z.array(z.string().trim().min(1)).optional(),
  ticketCount: z.number().int().nonnegative().optional(),
};

const bookingCancelledEventSchema = z
  .object({
    eventId: z.string().uuid(),
    occurredAt: z.string().datetime({ offset: true }),
    ...bookingCancelledFields,
  })
  .strict();
export type BookingCancelledEvent = z.infer<typeof bookingCancelledEventSchema>;

export const OperationalBookingCancelledEventSchema = z
  .object({
    eventId: z.string().uuid(),
    occurredAt: z.string().datetime({ offset: true }),
    ...bookingCancelledFields,
    bookingCode: z.string().trim().min(1),
    ticketCodes: z.array(z.string().trim().min(1)),
    ticketCount: z.number().int().nonnegative(),
    tripId: z.string().uuid(),
    previousStatus: z.enum(['PENDING_PAYMENT', 'CONFIRMED']),
    seatNumbers: z.array(z.string().trim().min(1)),
  })
  .strict()
  .refine((event) => event.ticketCount === event.ticketCodes.length, {
    message: 'ticketCount must equal ticketCodes length',
    path: ['ticketCount'],
  });
export type OperationalBookingCancelledEvent = z.infer<
  typeof OperationalBookingCancelledEventSchema
>;

const bookingCancelledLegacyEventSchema = z.object(bookingCancelledFields).strict();
export type BookingCancelledLegacyEvent = z.infer<typeof bookingCancelledLegacyEventSchema>;

const bookingCancelledConsumerEventSchema = z.union([
  OperationalBookingCancelledEventSchema,
  bookingCancelledEventSchema,
  bookingCancelledLegacyEventSchema,
]);
export type BookingCancelledConsumerEvent = z.infer<typeof bookingCancelledConsumerEventSchema>;

export {
  bookingCancelledConsumerEventSchema as BookingCancelledConsumerEventSchema,
  bookingCancelledEventSchema as BookingCancelledEventSchema,
  bookingCancelledLegacyEventSchema as BookingCancelledLegacyEventSchema,
};

export const BOOKING_CANCELLED_ROUTING_KEY = 'booking.booking.cancelled';
