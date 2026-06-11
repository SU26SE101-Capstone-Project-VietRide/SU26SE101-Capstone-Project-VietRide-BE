export const EMAIL_SEND_QUEUE_NAME = 'email-send';
export const EMAIL_SEND_JOB_NAME = 'send-email';
export const EMAIL_SEND_ATTEMPTS = 4;
export const EMAIL_SEND_BACKOFF_DELAYS_MS = [5_000, 30_000, 300_000] as const;
export const LAST_EMAIL_SEND_BACKOFF_DELAY_MS = 300_000;
export const EMAIL_LAST_ERROR_MAX_LENGTH = 1_000;
