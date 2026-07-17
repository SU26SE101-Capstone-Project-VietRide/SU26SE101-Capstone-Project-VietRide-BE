import { z } from 'zod';

export const TripCancelledEventSchema = z
  .object({
    eventId: z.string().uuid(),
    occurredAt: z.string().datetime({ offset: true }),
    tripId: z.string().uuid(),
    operatorId: z.string().uuid(),
    cancelledAt: z.string().datetime({ offset: true }),
    cancelReason: z.string(),
  })
  .strict();
export type TripCancelledEvent = z.infer<typeof TripCancelledEventSchema>;

export const TRIP_CANCELLED_ROUTING_KEY = 'trip.trip.cancelled';
