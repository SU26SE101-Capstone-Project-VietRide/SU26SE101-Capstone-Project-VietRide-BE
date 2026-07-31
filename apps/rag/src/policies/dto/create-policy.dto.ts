import { z } from 'zod';

const requiredTrimmedText = z.string().trim().min(1);

export const CreatePolicySchema = z
  .object({
    title: requiredTrimmedText,
    description: requiredTrimmedText,
    content: requiredTrimmedText,
    policyType: z.enum(['FOR_OPERATOR', 'FOR_USER']),
    category: requiredTrimmedText,
    active: z.boolean(),
  })
  .strict();

export type CreatePolicyDto = z.infer<typeof CreatePolicySchema>;
