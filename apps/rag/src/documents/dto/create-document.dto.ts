import { z } from 'zod';

const commaSeparatedStringArraySchema = z.preprocess((value) => {
  if (Array.isArray(value)) return value;
  if (typeof value !== 'string') return value;
  const trimmed = value.trim();
  if (trimmed.length === 0) return [];
  if (trimmed.startsWith('[')) {
    try {
      return JSON.parse(trimmed) as unknown;
    } catch {
      return value;
    }
  }
  return trimmed
    .split(',')
    .map((item) => item.trim())
    .filter((item) => item.length > 0);
}, z.array(z.string().min(1)).default([]));

const optionalUuidSchema = z.preprocess((value) => {
  if (value === '' || value === 'null') return undefined;
  return value;
}, z.string().uuid().optional());

export const CreateDocumentSchema = z.object({
  title: z.string().trim().min(1).max(500),
  description: z
    .preprocess((value) => (value === '' ? undefined : value), z.string().trim().optional()),
  accessLevel: z.enum(['PUBLIC', 'OPERATOR', 'ADMIN']),
  operatorId: optionalUuidSchema,
  category: z.enum(['CUSTOMER_SUPPORT', 'OPERATOR_POLICY', 'PLATFORM_ADMIN']),
  documentType: z.enum(['FAQ', 'POLICY', 'SOP', 'GUIDE', 'TERMS']),
  audienceRoles: commaSeparatedStringArraySchema,
  language: z.literal('vi').default('vi'),
});

export type CreateDocumentDto = z.infer<typeof CreateDocumentSchema>;
