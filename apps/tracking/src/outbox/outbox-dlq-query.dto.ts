import { z } from 'zod';

export const outboxDlqQuerySchema = z
  .object({
    eventType: z.string().trim().min(1).max(100).optional(),
    pageSize: z.coerce.number().int().min(1).max(100).default(100),
    afterTerminalAt: z.coerce.date().optional(),
    afterId: z.string().uuid().optional(),
    sortDir: z.enum(['asc', 'desc']).default('desc'),
  })
  .superRefine((value, context) => {
    if (Boolean(value.afterTerminalAt) !== Boolean(value.afterId)) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'afterTerminalAt and afterId must be provided together',
        path: ['afterTerminalAt'],
      });
    }
  });

export type OutboxDlqQueryDto = z.infer<typeof outboxDlqQuerySchema>;
