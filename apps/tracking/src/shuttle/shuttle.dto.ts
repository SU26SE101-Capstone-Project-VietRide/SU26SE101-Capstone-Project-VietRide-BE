import { z } from 'zod';

export const JoinShuttleTrackingSchema = z.object({
  shuttleTripId: z.string().uuid(),
});

export const ShuttleTripIdParamSchema = JoinShuttleTrackingSchema;
export type ShuttleTripIdParamDto = z.infer<typeof ShuttleTripIdParamSchema>;

export const ShuttleGpsUpdateSchema = z.object({
  shuttleTripId: z.string().uuid(),
  latitude: z.number().min(-90).max(90),
  longitude: z.number().min(-180).max(180),
  speedKmh: z.number().min(0).optional(),
  heading: z.number().min(0).max(360).optional(),
  recordedAt: z.string().datetime(),
});

export type ShuttleGpsUpdateDto = z.infer<typeof ShuttleGpsUpdateSchema>;
