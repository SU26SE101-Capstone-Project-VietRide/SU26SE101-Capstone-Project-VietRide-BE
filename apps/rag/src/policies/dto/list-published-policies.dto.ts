import { z, type ZodSchema } from 'zod';

const optionalTrimmedText = z.preprocess(
  (value) => (typeof value === 'string' && value.trim().length === 0 ? undefined : value),
  z.string().trim().min(1).optional(),
);

export const ListPublishedPoliciesQuerySchema = z
  .object({
    operatorId: z.string().trim().uuid().optional(),
    category: optionalTrimmedText,
    search: optionalTrimmedText,
    page: z.coerce.number().int().min(1).default(1),
    pageSize: z.coerce.number().int().min(1).max(100).default(20),
    sortBy: z.enum(['updatedAt', 'createdAt', 'title', 'version']).default('updatedAt'),
    sortDir: z.enum(['asc', 'desc']).default('desc'),
  })
  .strict();

export type ListPublishedPoliciesQueryDto = z.infer<typeof ListPublishedPoliciesQuerySchema>;
export const ListPublishedPoliciesValidationSchema =
  ListPublishedPoliciesQuerySchema as unknown as ZodSchema<ListPublishedPoliciesQueryDto>;
