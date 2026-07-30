import {
  createCipheriv,
  createDecipheriv,
  createHash,
  randomBytes,
} from 'node:crypto';
import type {
  EmailSendJobData,
  EmailSendQueueData,
  EmailTemplateData,
  EncryptedEmailTemplateData,
} from './email-send.types';

const ALGORITHM = 'aes-256-gcm';
const ENCRYPTION_VERSION = 1;
const IV_LENGTH_BYTES = 12;
const KEY_CONTEXT = 'vietride-notification-email-queue-v1';

export function encryptEmailSendJob(
  data: EmailSendQueueData,
  internalJwtSecret: string | undefined,
): EmailSendJobData {
  const key = deriveKey(internalJwtSecret);
  const iv = randomBytes(IV_LENGTH_BYTES);
  const cipher = createCipheriv(ALGORITHM, key, iv);
  cipher.setAAD(createAdditionalAuthenticatedData(data));

  const plaintext = Buffer.from(JSON.stringify(data.templateData), 'utf8');
  const ciphertext = Buffer.concat([cipher.update(plaintext), cipher.final()]);

  return {
    emailDeliveryId: data.emailDeliveryId,
    toEmail: data.toEmail,
    templateKey: data.templateKey,
    encryptedTemplateData: {
      version: ENCRYPTION_VERSION,
      iv: iv.toString('base64'),
      authTag: cipher.getAuthTag().toString('base64'),
      ciphertext: ciphertext.toString('base64'),
    },
  };
}

export function decryptEmailTemplateData(
  data: EmailSendJobData,
  internalJwtSecret: string | undefined,
): EmailTemplateData {
  if (data.encryptedTemplateData.version !== ENCRYPTION_VERSION) {
    throw new Error('EMAIL_QUEUE_ENCRYPTION_VERSION_UNSUPPORTED');
  }

  const key = deriveKey(internalJwtSecret);
  const encrypted = data.encryptedTemplateData;
  const decipher = createDecipheriv(ALGORITHM, key, Buffer.from(encrypted.iv, 'base64'));
  decipher.setAAD(createAdditionalAuthenticatedData(data));
  decipher.setAuthTag(Buffer.from(encrypted.authTag, 'base64'));

  const plaintext = Buffer.concat([
    decipher.update(Buffer.from(encrypted.ciphertext, 'base64')),
    decipher.final(),
  ]);
  return JSON.parse(plaintext.toString('utf8')) as EmailTemplateData;
}

function deriveKey(internalJwtSecret: string | undefined): Buffer {
  if (!internalJwtSecret) {
    throw new Error('INTERNAL_JWT_SECRET_REQUIRED_FOR_EMAIL_QUEUE_ENCRYPTION');
  }

  return createHash('sha256')
    .update(KEY_CONTEXT, 'utf8')
    .update('\0', 'utf8')
    .update(internalJwtSecret, 'utf8')
    .digest();
}

function createAdditionalAuthenticatedData(
  data: Pick<EmailSendQueueData, 'emailDeliveryId' | 'toEmail' | 'templateKey'>,
): Buffer {
  return Buffer.from(
    `${data.emailDeliveryId}\0${data.toEmail}\0${data.templateKey}`,
    'utf8',
  );
}

export function isEncryptedEmailTemplateData(
  value: unknown,
): value is EncryptedEmailTemplateData {
  if (typeof value !== 'object' || value === null) return false;
  const candidate = value as Partial<EncryptedEmailTemplateData>;
  return (
    candidate.version === ENCRYPTION_VERSION &&
    typeof candidate.iv === 'string' &&
    typeof candidate.authTag === 'string' &&
    typeof candidate.ciphertext === 'string'
  );
}
