import { z } from 'zod';

export const JoinTripTrackingSchema = z.object({
  tripId: z.string().uuid(),
});

export type JoinTripTrackingDto = z.infer<typeof JoinTripTrackingSchema>;
