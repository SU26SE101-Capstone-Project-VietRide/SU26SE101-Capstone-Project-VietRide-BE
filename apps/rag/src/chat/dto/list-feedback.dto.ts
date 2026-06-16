import { z } from 'zod';

export const ListFeedbackQuerySchema = z.object({
  page: z.coerce.number().int().positive().default(1),
  pageSize: z.coerce.number().int().positive().max(100).default(20),
  sortBy: z.enum(['createdAt', 'rating']).default('createdAt'),
  sortDir: z.enum(['asc', 'desc']).default('desc'),
});

export type ListFeedbackQueryDto = z.infer<typeof ListFeedbackQuerySchema>;
