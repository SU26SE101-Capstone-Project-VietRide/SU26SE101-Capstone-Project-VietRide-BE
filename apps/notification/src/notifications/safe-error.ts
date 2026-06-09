const BEARER_TOKEN_PATTERN = /(bearer\s+)[^\s]+/gi;
const SENSITIVE_KEY_VALUE_PATTERN =
  /([A-Za-z0-9_]*(?:otp|code|token|secret|password|deliveryToken|emailBody|body)[A-Za-z0-9_]*)=([^&\s]+)/gi;
const REDACTED_TEXT = '[REDACTED]';

export function normalizeSafeError(error: unknown, maxLength: number): string {
  const message = error instanceof Error ? error.message : String(error);

  return message
    .replace(BEARER_TOKEN_PATTERN, redactBearerToken)
    .replace(SENSITIVE_KEY_VALUE_PATTERN, redactSensitiveKeyValue)
    .slice(0, maxLength);
}

function redactBearerToken(_match: string, bearerPrefix: string): string {
  return `${bearerPrefix}${REDACTED_TEXT}`;
}

function redactSensitiveKeyValue(_match: string, key: string): string {
  return `${key}=${REDACTED_TEXT}`;
}
