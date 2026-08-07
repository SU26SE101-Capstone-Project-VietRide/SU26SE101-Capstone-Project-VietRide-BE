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
  targetKind: z.enum(['STOP', 'STATION']).optional(),
  stopId: z.string().uuid().optional(),
  stationId: z.string().uuid().optional(),
}).superRefine((query, context) => {
  const validLegacy = query.targetKind === undefined
    && query.stationId === undefined;
  const validStop = query.targetKind === 'STOP'
    && query.stopId !== undefined
    && query.stationId === undefined;
  const validStation = query.targetKind === 'STATION'
    && query.stationId !== undefined
    && query.stopId === undefined;
  if (!validLegacy && !validStop && !validStation) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      path: ['targetKind'],
      message: 'targetKind must be paired with exactly one matching stopId or stationId',
    });
  }
});

export type EtaQueryDto = z.infer<typeof EtaQuerySchema>;
