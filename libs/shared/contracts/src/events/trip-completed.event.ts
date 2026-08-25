import { z } from 'zod';

export const TripCompletedEventSchema = z.object({
  eventId: z.string().uuid(),
  occurredAt: z.string().datetime({ offset: true }),
  tripId: z.string().uuid(),
  operatorId: z.string().uuid(),
  terminalAt: z.string().datetime({ offset: true }),
  hasSubstitution: z.boolean(),
  tripCode: z.string().trim().min(1).optional().nullable(),
  source: z.enum(['MANUAL', 'AUTO_FROM_SCHEDULE', 'VEHICLE_SUBSTITUTION']).optional().nullable(),
});

export type TripCompletedEvent = z.infer<typeof TripCompletedEventSchema>;

// <service>.<aggregate>.<verb_past> per BACKEND_SOURCE_OF_TRUTH §7.3.
export const TRIP_COMPLETED_ROUTING_KEY = 'trip.trip.completed';

export const TripDisruptedEventSchema = TripCompletedEventSchema.extend({
  reason: z.string().trim().min(1).optional(),
});
export type TripDisruptedEvent = z.infer<typeof TripDisruptedEventSchema>;
export const TRIP_DISRUPTED_ROUTING_KEY = 'trip.trip.disrupted';
