import { z } from 'zod';
import { NotificationType } from '../../generated/notification-prisma-client';

export const MAX_NOTIFICATION_TITLE_LENGTH = 255;

export const CreateNotificationSchema = z.object({
  userId: z.string().uuid(),
  type: z.nativeEnum(NotificationType),
  title: z.string().trim().min(1).max(MAX_NOTIFICATION_TITLE_LENGTH),
  body: z.string().trim().min(1),
  data: z.unknown().nullable().optional().transform((value) => value ?? null),
  dedupeKey: z.string().trim().min(1).max(200).optional(),
});

export type CreateNotificationDto = z.input<typeof CreateNotificationSchema>;
export type NormalizedCreateNotificationDto = z.output<typeof CreateNotificationSchema>;
