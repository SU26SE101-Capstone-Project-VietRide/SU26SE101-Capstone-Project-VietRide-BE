import { z } from 'zod';

import { TripVehicleSwapSeatImpactReasonSchema } from './trip-vehicle-swapped.event';

export const BookingPendingActionEventFieldsSchema = z
  .object({
    eventId: z.string().uuid(),
    occurredAt: z.string().datetime({ offset: true }),
    bookingId: z.string().uuid(),
    tripId: z.string().uuid(),
    userId: z.string().uuid(),
    pendingActionId: z.string().uuid(),
    deadline: z.string().datetime({ offset: true }),
  })
  .strict();

export const BookingSeatReassignmentRequiredEventSchema =
  BookingPendingActionEventFieldsSchema.extend({
    seatNumbers: z.array(z.string()),
    reason: TripVehicleSwapSeatImpactReasonSchema,
  }).strict();
export type BookingSeatReassignmentRequiredEvent = z.infer<
  typeof BookingSeatReassignmentRequiredEventSchema
>;

export const BOOKING_SEAT_REASSIGNMENT_REQUIRED_ROUTING_KEY =
  'booking.booking.seat_reassignment_required';
