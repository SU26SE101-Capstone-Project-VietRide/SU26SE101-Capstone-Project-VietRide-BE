import { z } from 'zod';

export const BookingSeatShortageDetectedEventSchema = z
  .object({
    eventId: z.string().uuid(),
    occurredAt: z.string().datetime({ offset: true }),
    sourceSubstitutionEventId: z.string().uuid(),
    bookingId: z.string().uuid(),
    bookingCode: z.string().trim().min(1),
    operatorId: z.string().uuid(),
    oldTripId: z.string().uuid(),
    newTripId: z.string().uuid(),
    affectedPassengerCount: z.number().int().positive(),
    originalSeatNumbers: z.array(z.string().trim().min(1)),
  })
  .strict();

export type BookingSeatShortageDetectedEvent = z.infer<
  typeof BookingSeatShortageDetectedEventSchema
>;

export const BOOKING_SEAT_SHORTAGE_DETECTED_ROUTING_KEY =
  'booking.booking.seat_shortage_detected';
