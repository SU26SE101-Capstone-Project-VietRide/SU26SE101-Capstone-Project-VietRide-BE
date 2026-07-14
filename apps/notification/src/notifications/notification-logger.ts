import pino, { type DestinationStream, type Logger } from 'pino';

const PII_REDACTION_PATHS = [
  'email',
  '*.email',
  'toEmail',
  '*.toEmail',
  'payload',
  '*.payload',
  'error',
  '*.error',
  'err',
  '*.err',
  'lastError',
  '*.lastError',
  'downloadApiUrl',
  '*.downloadApiUrl',
  'signedUrl',
  '*.signedUrl',
  'downloadUrl',
  '*.downloadUrl',
  'req.headers.authorization',
] as const;

export function createNotificationLogger(name: string, destination?: DestinationStream): Logger {
  return pino(
    {
      name,
      redact: {
        paths: [...PII_REDACTION_PATHS],
        censor: '[REDACTED]',
      },
    },
    destination,
  );
}
