import { z } from 'zod';

const bookingDisruptedEventSchema = z
  .object({
    eventId: z.string().uuid(),
    occurredAt: z.string().datetime({ offset: true }),
    bookingId: z.string().uuid(),
    bookingCode: z.string().trim().min(1),
    tripId: z.string().uuid(),
    operatorId: z.string().uuid(),
    userId: z.string().uuid(),
    traveledRatio: z.number().min(0).max(1),
    refundAmount: z.number().int().nonnegative(),
    cancellationReason: z.string().trim().min(1),
  })
  .strict();
export type BookingDisruptedEvent = z.infer<typeof bookingDisruptedEventSchema>;

export { bookingDisruptedEventSchema as BookingDisruptedEventSchema };

export const BOOKING_DISRUPTED_ROUTING_KEY = 'booking.booking.disrupted';
