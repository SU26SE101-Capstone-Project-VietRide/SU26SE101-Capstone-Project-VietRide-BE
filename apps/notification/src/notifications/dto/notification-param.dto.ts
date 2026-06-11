import { z } from 'zod';

export const NotificationIdParamSchema = z.object({
  notificationId: z.string().uuid(),
});

export type NotificationIdParamDto = z.infer<typeof NotificationIdParamSchema>;
