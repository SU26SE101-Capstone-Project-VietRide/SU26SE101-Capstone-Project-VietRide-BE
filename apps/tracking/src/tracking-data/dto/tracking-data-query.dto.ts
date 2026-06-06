import { z } from 'zod';

const DEFAULT_TRAIL_LIMIT = 500;
const MAX_TRAIL_LIMIT = 1_000;

export const TripIdParamSchema = z.object({
  tripId: z.string().uuid(),
});

export type TripIdParamDto = z.infer<typeof TripIdParamSchema>;

export const TrailQuerySchema = z
  .object({
    from: z.string().datetime().optional(),
    to: z.string().datetime().optional(),
    limit: z.coerce.number().int().positive().max(MAX_TRAIL_LIMIT).default(DEFAULT_TRAIL_LIMIT),
  })
  .refine(
    (query) => {
      if (!query.from || !query.to) return true;
      return new Date(query.from).getTime() <= new Date(query.to).getTime();
    },
    {
      message: 'from must be before or equal to to',
      path: ['from'],
    },
  );

export type TrailQueryDto = z.infer<typeof TrailQuerySchema>;

export const EtaQuerySchema = z.object({
  stopId: z.string().uuid(),
});

export type EtaQueryDto = z.infer<typeof EtaQuerySchema>;
