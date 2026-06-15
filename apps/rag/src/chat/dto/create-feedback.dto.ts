import { z } from 'zod';

export const MessageIdParamSchema = z.string().uuid();

export const CreateFeedbackSchema = z.object({
  rating: z.number().int().min(-1).max(1).refine((value) => value !== 0, {
    message: 'rating must be -1 or 1',
  }),
});

export type CreateFeedbackDto = z.infer<typeof CreateFeedbackSchema>;
export type MessageIdParamDto = z.infer<typeof MessageIdParamSchema>;
