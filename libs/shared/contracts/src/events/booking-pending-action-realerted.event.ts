import { z } from 'zod';

import { BookingPendingActionEventFieldsSchema } from './booking-seat-reassignment-required.event';
import { BookingRequiredScheduleChangeSeveritySchema } from './booking-schedule-change-required.event';
import { TripVehicleSwapSeatImpactReasonSchema } from './trip-vehicle-swapped.event';

export const BookingPendingActionRealertedEventSchema = z.discriminatedUnion('reason', [
  BookingPendingActionEventFieldsSchema.extend({
    reason: z.literal('PENDING_SEAT_ASSIGNMENT'),
    seatNumbers: z.array(z.string()),
    seatImpactReason: TripVehicleSwapSeatImpactReasonSchema,
  }).strict(),
  BookingPendingActionEventFieldsSchema.extend({
    reason: z.literal('SCHEDULE_CHANGE'),
    oldDeparture: z.string().datetime({ offset: true }),
    newDeparture: z.string().datetime({ offset: true }),
    severity: BookingRequiredScheduleChangeSeveritySchema,
  }).strict(),
]);
export type BookingPendingActionRealertedEvent = z.infer<
  typeof BookingPendingActionRealertedEventSchema
>;

export const BOOKING_PENDING_ACTION_REALERTED_ROUTING_KEY =
  'booking.booking.pending_action_realerted';
