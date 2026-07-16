import { z } from 'zod';

/** Published by Booking only for CONFIRMED Bookings affected by a MINOR schedule change. */
export const BookingScheduleChangeInformationalEventSchema = z
  .object({
    eventId: z.string().uuid(),
    occurredAt: z.string().datetime({ offset: true }),
    bookingId: z.string().uuid(),
    tripId: z.string().uuid(),
    userId: z.string().uuid(),
    oldDeparture: z.string().datetime({ offset: true }),
    newDeparture: z.string().datetime({ offset: true }),
    severity: z.literal('MINOR'),
  })
  .strict();
export type BookingScheduleChangeInformationalEvent = z.infer<
  typeof BookingScheduleChangeInformationalEventSchema
>;

export const BOOKING_SCHEDULE_CHANGE_INFORMATIONAL_ROUTING_KEY =
  'booking.booking.schedule_change_informational';
