import { z, type ZodTypeAny } from 'zod';

/**
 * 7-field paginated list envelope per ADR 0004 / BSOT section 5.7.
 * `totalPages`, `hasNextPage`, `hasPreviousPage` are computed server-side.
 */
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

/**
 * Backward-compatible alias — existing consumers that used the 4-field
 * `PageResult<T>` can keep importing it until migrated.
 */
export type PageResult<T> = PagedResult<T>;

export function pagedResultSchema<T extends ZodTypeAny>(item: T) {
  return z.object({
    items: z.array(item),
    page: z.number().int().nonnegative(),
    pageSize: z.number().int().positive(),
    totalItems: z.number().int().nonnegative(),
    totalPages: z.number().int().nonnegative(),
    hasNextPage: z.boolean(),
    hasPreviousPage: z.boolean(),
  });
}

/**
 * Backward-compatible alias.
 */
export const pageResultSchema = pagedResultSchema;
