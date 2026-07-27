import { redactSensitiveText } from './sensitive-data-redaction';

export function normalizeSafeError(error: unknown, maxLength: number): string {
  const message = error instanceof Error ? error.message : String(error);

  return redactSensitiveText(message).slice(0, maxLength);
}
