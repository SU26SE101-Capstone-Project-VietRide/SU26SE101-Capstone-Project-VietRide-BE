import { z } from 'zod';

const OptionalUuidSchema = z
  .string()
  .trim()
  .transform((value) => (value.length === 0 || value === 'null' ? undefined : value))
  .optional()
  .refine((value) => value === undefined || z.string().uuid().safeParse(value).success, {
    message: 'Invalid UUID',
  });

export const ListDocumentsQuerySchema = z.object({
  page: z.coerce.number().int().positive().default(1),
  pageSize: z.coerce.number().int().positive().max(100).default(20),
  sortBy: z.enum(['createdAt', 'updatedAt', 'title', 'status', 'ingestStatus']).default('createdAt'),
  sortDir: z.enum(['asc', 'desc']).default('desc'),
  status: z.enum(['PENDING_REVIEW', 'APPROVED', 'REJECTED', 'ARCHIVED']).optional(),
  ingestStatus: z.enum(['PENDING', 'PROCESSING', 'COMPLETED', 'FAILED']).optional(),
  accessLevel: z.enum(['PUBLIC', 'OPERATOR', 'ADMIN']).optional(),
  category: z.enum(['CUSTOMER_SUPPORT', 'OPERATOR_POLICY', 'PLATFORM_ADMIN']).optional(),
  documentType: z.enum(['FAQ', 'POLICY', 'SOP', 'GUIDE', 'TERMS']).optional(),
  operatorId: OptionalUuidSchema,
  q: z
    .string()
    .trim()
    .transform((value) => (value.length === 0 ? undefined : value))
    .optional(),
});

export type ListDocumentsQueryDto = z.infer<typeof ListDocumentsQuerySchema>;
