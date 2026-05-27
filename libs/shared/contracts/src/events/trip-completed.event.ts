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

export const TRIP_COMPLETED_ROUTING_KEY = 'trip.completed';
