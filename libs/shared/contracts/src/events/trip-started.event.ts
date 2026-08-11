import { z } from 'zod';

export const TripStartedEventSchema = z
  .object({
    tripId: z.string().uuid(),
    actualDepartureTime: z.string().datetime({ offset: true }),
  })
  .strict();

export type TripStartedEvent = z.infer<typeof TripStartedEventSchema>;

export const TRIP_STARTED_ROUTING_KEY = 'trip.trip.started';
