import type { EmailTemplateData } from './email-send.types';

const SENSITIVE_KEY_PATTERN = /(code|otp|token|secret|password|url|link)/i;
const MASK_HEAD_LENGTH = 4;
const MASK_TAIL_LENGTH = 4;

export function sanitizeEmailTemplateData(data: EmailTemplateData): EmailTemplateData {
  return sanitizeValue(data) as EmailTemplateData;
}

function sanitizeValue(value: unknown): unknown {
  if (Array.isArray(value)) {
    return value.map((item) => sanitizeValue(item));
  }

  if (typeof value === 'object' && value !== null) {
    const sanitized: Record<string, unknown> = {};
    for (const [key, nestedValue] of Object.entries(value)) {
      sanitized[key] = SENSITIVE_KEY_PATTERN.test(key)
        ? maskSensitiveValue(nestedValue)
        : sanitizeValue(nestedValue);
    }

    return sanitized;
  }

  return value;
}

function maskSensitiveValue(value: unknown): string {
  if (value === null || value === undefined) {
    return '[REDACTED]';
  }

  const text = String(value);
  if (text.length <= MASK_HEAD_LENGTH + MASK_TAIL_LENGTH) {
    return '[REDACTED]';
  }

  return `${text.slice(0, MASK_HEAD_LENGTH)}...[REDACTED]...${text.slice(-MASK_TAIL_LENGTH)}`;
}
