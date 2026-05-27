import { z } from 'zod';

export const TripCompletedEventSchema = z.object({
  eventId: z.string().uuid(),
  occurredAt: z.string().datetime({ offset: true }),
  tripId: z.string().uuid(),
  fareVnd: z.number().int().nonnegative(),
  driverId: z.string().uuid(),
  passengerId: z.string().uuid(),
});

export type TripCompletedEvent = z.infer<typeof TripCompletedEventSchema>;

// <service>.<aggregate>.<verb_past> per BACKEND_SOURCE_OF_TRUTH §7.3.
export const TRIP_COMPLETED_ROUTING_KEY = 'trip.trip.completed';
