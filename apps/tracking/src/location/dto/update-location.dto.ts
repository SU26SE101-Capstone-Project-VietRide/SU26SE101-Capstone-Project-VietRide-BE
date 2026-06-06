import { z } from 'zod';

export const UpdateLocationSchema = z.object({
  tripId: z.string().uuid(),
  latitude: z.number().min(-90).max(90),
  longitude: z.number().min(-180).max(180),
  speedKmh: z.number().min(0).optional(),
  headingDeg: z.number().min(0).max(360).optional(),
  recordedAt: z.string().datetime(),
});

export type UpdateLocationDto = z.infer<typeof UpdateLocationSchema>;
