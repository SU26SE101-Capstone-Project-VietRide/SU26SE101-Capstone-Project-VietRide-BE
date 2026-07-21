import { z } from 'zod';

const tripCargoThresholdCrossedEventSchema = z
  .object({
    eventId: z.string().uuid(),
    occurredAt: z.string().datetime({ offset: true }),
    tripId: z.string().uuid(),
    operatorId: z.string().uuid(),
    loadedWeightKg: z.number().nonnegative(),
    maxCargoWeightKg: z.number().positive(),
    percentFull: z.number().nonnegative(),
  })
  .strict();

export type TripCargoThresholdCrossedEvent = z.infer<
  typeof tripCargoThresholdCrossedEventSchema
>;

export { tripCargoThresholdCrossedEventSchema as TripCargoThresholdCrossedEventSchema };

export const TRIP_CARGO_THRESHOLD_CROSSED_ROUTING_KEY = 'trip.cargo.threshold_crossed';
