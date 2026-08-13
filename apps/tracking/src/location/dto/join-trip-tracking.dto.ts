import { z } from 'zod';

export const JoinTripTrackingSchema = z.object({
  tripId: z.string().uuid(),
  includeRouteSnapshot: z.boolean().optional().default(false),
});

export type JoinTripTrackingDto = z.infer<typeof JoinTripTrackingSchema>;
