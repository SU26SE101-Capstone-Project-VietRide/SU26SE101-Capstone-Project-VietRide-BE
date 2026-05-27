import { z, type ZodTypeAny } from 'zod';

/**
 * Generic paginated list envelope used across VietRide list endpoints.
 */
export interface PageResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
}

export function pageResultSchema<T extends ZodTypeAny>(item: T) {
  return z.object({
    items: z.array(item),
    page: z.number().int().nonnegative(),
    pageSize: z.number().int().positive(),
    total: z.number().int().nonnegative(),
  });
}
