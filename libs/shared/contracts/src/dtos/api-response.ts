import { z } from 'zod';

/**
 * Shared response envelope for every VietRide FE-facing HTTP endpoint.
 * Mirrors the .NET `ApiResponse<T>` / `ApiResponse` shape per ADR 0004.
 *
 * Shape:
 *  Success: { success: true, statusCode, message?, data, meta }
 *  Error:   { success: false, statusCode, error, meta }
 */

// ── Meta ──────────────────────────────────────────────────────────────────────

export const ApiMetaSchema = z.object({
  /** Correlation id stamped by the gateway (X-Request-Id / ADR 0002). */
  traceId: z.string().optional(),
  /** Response timestamp — UTC ISO-8601. */
  timestamp: z.string(),
});

export type ApiMeta = z.infer<typeof ApiMetaSchema>;

// ── Error sub-shapes ──────────────────────────────────────────────────────────

export const ApiFieldErrorSchema = z.object({
  field: z.string(),
  message: z.string(),
});

export type ApiFieldError = z.infer<typeof ApiFieldErrorSchema>;

export const ApiErrorSchema = z.object({
  /** BSOT section 5.9 registry code (UPPER_SNAKE_CASE). */
  code: z.string(),
  message: z.string(),
  fields: z.array(ApiFieldErrorSchema).optional(),
});

export type ApiError = z.infer<typeof ApiErrorSchema>;

// ── Success envelope (generic) ────────────────────────────────────────────────

/**
 * Builds a Zod schema for the success envelope with a specific `data` shape.
 *
 * ```ts
 * const schema = apiResponseSchema(z.object({ userId: z.string() }));
 * ```
 */
export function apiResponseSchema<T extends z.ZodTypeAny>(dataSchema: T) {
  return z.object({
    success: z.literal(true),
    statusCode: z.number().int(),
    message: z.string().optional(),
    data: dataSchema,
    meta: ApiMetaSchema,
  });
}

/**
 * TypeScript type for the success envelope.
 * Use `ApiResponse<YourDto>` for typed access.
 */
export type ApiResponse<T> = {
  success: true;
  statusCode: number;
  message?: string;
  data: T;
  meta: ApiMeta;
};

// ── Error envelope ────────────────────────────────────────────────────────────

export const ApiResponseErrorSchema = z.object({
  success: z.literal(false),
  statusCode: z.number().int(),
  error: ApiErrorSchema,
  meta: ApiMetaSchema,
});

export type ApiResponseError = z.infer<typeof ApiResponseErrorSchema>;

// ── Combined (for consumers that parse either shape) ──────────────────────────

/**
 * Union type — either a success envelope with `data` of type `T`, or an error
 * envelope with `error`.  Useful for generic response parsing.
 */
export type ApiResponseOrError<T> = ApiResponse<T> | ApiResponseError;
