import { z } from 'zod';

export const CreateChatSchema = z.object({
  conversationId: z.string().uuid().optional(),
  message: z.string().trim().min(1).max(4_000),
  operatorId: z.string().uuid().optional(),
});

export type CreateChatDto = z.infer<typeof CreateChatSchema>;
