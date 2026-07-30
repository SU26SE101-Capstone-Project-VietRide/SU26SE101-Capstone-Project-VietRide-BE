import type { EmailTemplateData } from './email-send.types';

const SENSITIVE_KEY_PATTERN = /(code|otp|token|secret|password|url|link)/i;

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
        ? maskSensitiveValue()
        : sanitizeValue(nestedValue);
    }

    return sanitized;
  }

  return value;
}

function maskSensitiveValue(): string {
  return '[REDACTED]';
}
