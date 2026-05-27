import { z } from 'zod';

/**
 * RFC 7807 Problem Details envelope used by every VietRide API.
 * Per VietRide_API_Contract_v1 §"Error envelope".
 */
export const ProblemDetailsSchema = z.object({
  type: z.string().url(),
  title: z.string(),
  status: z.number().int(),
  detail: z.string().optional(),
  instance: z.string().optional(),
  traceId: z.string().optional(),
  errorCode: z.string().optional(),
  // Field-level validation detail — per BACKEND_SOURCE_OF_TRUTH §5.5/§5.9 the
  // canonical key is `errors` (appears with VALIDATION_ERROR 422).
  errors: z
    .array(
      z.object({
        path: z.string(),
        code: z.string(),
        message: z.string(),
      }),
    )
    .optional(),
});

export type ProblemDetails = z.infer<typeof ProblemDetailsSchema>;
