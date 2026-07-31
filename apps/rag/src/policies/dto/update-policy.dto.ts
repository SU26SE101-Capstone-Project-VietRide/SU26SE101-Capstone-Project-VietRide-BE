import { z } from 'zod';

const optionalTrimmedText = z.string().trim().min(1).optional();

export const UpdatePolicySchema = z
  .object({
    version: z.number().int().positive(),
    title: optionalTrimmedText,
    description: optionalTrimmedText,
    content: optionalTrimmedText,
    policyType: z.enum(['FOR_OPERATOR', 'FOR_USER']).optional(),
    category: optionalTrimmedText,
    active: z.boolean().optional(),
  })
  .strict()
  .refine(
    (value) =>
      value.title !== undefined ||
      value.description !== undefined ||
      value.content !== undefined ||
      value.policyType !== undefined ||
      value.category !== undefined ||
      value.active !== undefined,
    { message: 'At least one Policy field must be provided' },
  );

export type UpdatePolicyDto = z.infer<typeof UpdatePolicySchema>;
