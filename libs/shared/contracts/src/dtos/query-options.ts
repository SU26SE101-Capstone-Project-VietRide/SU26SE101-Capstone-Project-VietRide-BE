import { z } from 'zod';

/**
 * Shared query-string input for list/collection endpoints.
 * Per ADR 0004 Rule 6 / BSOT section 5.7.
 *
 * `sortBy` and `searchIn` are whitelist-validated per aggregate (security
 * requirement) — the repository/handler rejects any field not in its allow-list.
 */
export interface QueryOptions {
  /** 1-based page index. Default 1. */
  page: number;
  /** Items per page. Default 20, clamped 1..100. */
  pageSize: number;
  /** Free-text search term. */
  search?: string;
  /** Comma-separated fields to search in (whitelist per aggregate). */
  searchIn?: string;
  /** Field to sort by (whitelist per aggregate). */
  sortBy?: string;
  /** Sort direction. Default 'desc'. */
  sortDir: 'asc' | 'desc';
  /** Include soft-deleted records. Default false. */
  includeDeleted: boolean;
}

export const QueryOptionsSchema = z.object({
  page: z.coerce.number().int().min(1).default(1),
  pageSize: z.coerce
    .number()
    .int()
    .default(20)
    .transform((value) => Math.min(100, Math.max(1, value))),
  search: z.string().optional(),
  searchIn: z.string().optional(),
  sortBy: z.string().optional(),
  sortDir: z.enum(['asc', 'desc']).default('desc'),
  includeDeleted: z.coerce.boolean().default(false),
});
