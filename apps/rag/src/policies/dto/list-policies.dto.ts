import { z, type ZodSchema } from 'zod';

const optionalTrimmedText = z.preprocess(
  (value) => (typeof value === 'string' && value.trim().length === 0 ? undefined : value),
  z.string().trim().min(1).optional(),
);

export const ListPoliciesQuerySchema = z
  .object({
    policyType: z.enum(['FOR_OPERATOR', 'FOR_USER']).optional(),
    category: optionalTrimmedText,
    active: z
      .enum(['true', 'false'])
      .transform((value) => value === 'true')
      .optional(),
    search: optionalTrimmedText,
    page: z.coerce.number().int().min(1).default(1),
    pageSize: z.coerce.number().int().min(1).max(100).default(20),
    sortBy: z.enum(['updatedAt', 'createdAt', 'title', 'version']).default('updatedAt'),
    sortDir: z.enum(['asc', 'desc']).default('desc'),
  })
  .strict();

export type ListPoliciesQueryDto = z.infer<typeof ListPoliciesQuerySchema>;
export const ListPoliciesValidationSchema =
  ListPoliciesQuerySchema as unknown as ZodSchema<ListPoliciesQueryDto>;
