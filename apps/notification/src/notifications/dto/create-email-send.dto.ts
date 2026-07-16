import { z } from 'zod';
import { EmailTemplateKey } from '../../generated/notification-prisma-client';

export const CreateEmailSendSchema = z.object({
  notificationId: z.string().uuid().nullable().optional(),
  dedupeKey: z.string().trim().min(1).max(200).optional(),
  toEmail: z.string().email(),
  templateKey: z.nativeEnum(EmailTemplateKey),
  templateData: z.record(z.unknown()),
});

export type CreateEmailSendDto = z.input<typeof CreateEmailSendSchema>;
export type NormalizedCreateEmailSendDto = z.output<typeof CreateEmailSendSchema>;
