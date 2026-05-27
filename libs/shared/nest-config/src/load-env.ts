import type { ZodSchema } from 'zod';

/**
 * Parse `process.env` (or any object) through a Zod schema.
 * Throws synchronously with a flattened error report if validation fails.
 */
export function loadEnv<T>(schema: ZodSchema<T>, raw: NodeJS.ProcessEnv | Record<string, unknown> = process.env): T {
  const parsed = schema.safeParse(raw);
  if (!parsed.success) {
    const flat = parsed.error.flatten();
    console.error('[nest-config] invalid environment:', JSON.stringify(flat.fieldErrors, null, 2));
    throw new Error('Invalid environment variables');
  }
  return parsed.data;
}
