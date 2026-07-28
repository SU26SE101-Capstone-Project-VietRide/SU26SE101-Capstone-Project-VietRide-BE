const REDACTED_TEXT = '[REDACTED]';
const BEARER_TOKEN_PATTERN = /(bearer\s+)[^\s]+/gi;
const JWT_PATTERN = /\b[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b/g;
const EMAIL_PATTERN = /\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b/gi;
const LABELED_SECRET_PATTERN =
  /((?:fcm[_ -]?token|device[_ -]?token|jwt|token|secret|password|otp(?:code)?|code)\s*[=:]\s*)[^&\s]+/gi;
const URL_PATTERN = /https?:\/\/[^\s"'<>]+/gi;
const SENSITIVE_URL_QUERY_KEYS = new Set([
  'x-goog-signature',
  'x-amz-signature',
  'signature',
  'sig',
  'token',
  'access_token',
  'auth',
]);

export function redactSensitiveText(value: string): string {
  return value
    .replace(BEARER_TOKEN_PATTERN, (_match, prefix: string) => `${prefix}${REDACTED_TEXT}`)
    .replace(LABELED_SECRET_PATTERN, (_match, prefix: string) => `${prefix}${REDACTED_TEXT}`)
    .replace(JWT_PATTERN, REDACTED_TEXT)
    .replace(EMAIL_PATTERN, REDACTED_TEXT)
    .replace(URL_PATTERN, redactSensitiveUrl);
}

export function redactSensitiveValue(value: unknown, key?: string): unknown {
  return redactValue(value, key, new WeakSet<object>());
}

function redactValue(value: unknown, key: string | undefined, seen: WeakSet<object>): unknown {
  if (key && isSensitiveKey(key)) return REDACTED_TEXT;
  if (typeof value === 'string') return redactSensitiveText(value);
  if (value === null || typeof value !== 'object') return value;
  if (value instanceof Date) return value;
  if (Buffer.isBuffer(value)) return REDACTED_TEXT;
  if (seen.has(value)) return REDACTED_TEXT;
  seen.add(value);

  if (value instanceof Error) {
    return {
      name: value.name,
      message: redactSensitiveText(value.message),
    };
  }

  if (Array.isArray(value)) {
    return value.map((item) => redactValue(item, undefined, seen));
  }

  return Object.fromEntries(
    Object.entries(value).map(([entryKey, entryValue]) => [
      entryKey,
      redactValue(entryValue, entryKey, seen),
    ]),
  );
}

function isSensitiveKey(key: string): boolean {
  const normalized = key.replace(/[^a-z0-9]/gi, '').toLowerCase();
  return (
    normalized === 'authorization' ||
    normalized === 'cookie' ||
    normalized === 'setcookie' ||
    normalized === 'body' ||
    normalized === 'data' ||
    isEmailKey(normalized) ||
    normalized.includes('token') ||
    normalized.includes('secret') ||
    normalized.includes('password') ||
    normalized.includes('privatekey') ||
    normalized.includes('payload') ||
    normalized.includes('signedurl') ||
    normalized.includes('downloadurl') ||
    normalized.startsWith('otp')
  );
}

function isEmailKey(normalized: string): boolean {
  return [
    'email',
    'toemail',
    'fromemail',
    'useremail',
    'senderemail',
    'recipientemail',
    'emailaddress',
  ].includes(normalized);
}

function redactSensitiveUrl(match: string): string {
  try {
    const url = new URL(match);
    const hasSensitiveQuery = [...url.searchParams.keys()].some((key) =>
      SENSITIVE_URL_QUERY_KEYS.has(key.toLowerCase()),
    );
    return hasSensitiveQuery ? REDACTED_TEXT : match;
  } catch {
    return match;
  }
}
