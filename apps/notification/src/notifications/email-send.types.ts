import type { EmailTemplateKey } from '../generated/notification-prisma-client';

export interface EmailSendJobData {
  emailDeliveryId: string;
  toEmail: string;
  templateKey: EmailTemplateKey;
  encryptedTemplateData: EncryptedEmailTemplateData;
}

export interface EmailSendQueueData {
  emailDeliveryId: string;
  toEmail: string;
  templateKey: EmailTemplateKey;
  templateData: EmailTemplateData;
}

export interface EncryptedEmailTemplateData {
  version: 1;
  iv: string;
  authTag: string;
  ciphertext: string;
}

export interface CreateEmailSendRequest {
  notificationId?: string | null;
  toEmail: string;
  templateKey: EmailTemplateKey;
  templateData: EmailTemplateData;
}

export type EmailTemplateData = Record<string, unknown>;

export interface RenderedEmail {
  subject: string;
  text: string;
  html: string;
}

export interface EmailProviderPayload extends RenderedEmail {
  deliveryId: string;
  toEmail: string;
}

export interface EmailProviderResult {
  messageId?: string;
}

export interface EmailProvider {
  send(payload: EmailProviderPayload): Promise<EmailProviderResult>;
}
