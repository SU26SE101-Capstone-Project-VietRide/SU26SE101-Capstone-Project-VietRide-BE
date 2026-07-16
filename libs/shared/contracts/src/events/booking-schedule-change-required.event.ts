import { z } from 'zod';

import { BookingPendingActionEventFieldsSchema } from './booking-seat-reassignment-required.event';

export const BookingRequiredScheduleChangeSeveritySchema = z.enum(['MEDIUM', 'MAJOR']);
export type BookingRequiredScheduleChangeSeverity = z.infer<
  typeof BookingRequiredScheduleChangeSeveritySchema
>;

/** Published by Booking only for CONFIRMED Bookings affected by a MEDIUM or MAJOR change. */
export const BookingScheduleChangeRequiredEventSchema =
  BookingPendingActionEventFieldsSchema.extend({
    oldDeparture: z.string().datetime({ offset: true }),
    newDeparture: z.string().datetime({ offset: true }),
    severity: BookingRequiredScheduleChangeSeveritySchema,
  }).strict();
export type BookingScheduleChangeRequiredEvent = z.infer<
  typeof BookingScheduleChangeRequiredEventSchema
>;

export const BOOKING_SCHEDULE_CHANGE_REQUIRED_ROUTING_KEY =
  'booking.booking.schedule_change_required';
