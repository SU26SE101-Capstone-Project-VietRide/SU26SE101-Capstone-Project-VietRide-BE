import { z } from 'zod';
import { QueryOptionsSchema } from '@vietride/contracts';

export const TripIdParamSchema = z.object({
  tripId: z.string().uuid(),
});

export type TripIdParamDto = z.infer<typeof TripIdParamSchema>;

export const TrailQuerySchema = z
  .object({
    from: z.string().datetime().optional(),
    to: z.string().datetime().optional(),
    sortBy: z.enum(['recordedAt']).default('recordedAt'),
    sortDir: z.enum(['asc', 'desc']).default('asc'),
  })
  .merge(
    QueryOptionsSchema.pick({ page: true, pageSize: true }),
  )
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
