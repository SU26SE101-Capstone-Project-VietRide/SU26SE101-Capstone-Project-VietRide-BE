import { z } from 'zod';

export const MarkNotificationReadSchema = z.object({
  read: z.literal(true),
});

export type MarkNotificationReadDto = z.infer<typeof MarkNotificationReadSchema>;
