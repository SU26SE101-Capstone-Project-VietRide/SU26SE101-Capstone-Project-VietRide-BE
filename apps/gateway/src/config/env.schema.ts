import { z } from 'zod';

// Docker compose passes "" for unset host vars (`${VAR:-}`); treat as absent.
const emptyToUndefined = (value: unknown): unknown => (value === '' ? undefined : value);

// Zod schema validating env at startup. Throws if INTERNAL_JWT_SECRET < 32 chars.
export const envSchema = z.object({
  GATEWAY_PORT: z.coerce.number().int().positive().default(3000),

  // Internal JWT (HS256, 120s) — shared with all 5 .NET services.
  INTERNAL_JWT_SECRET: z.string().min(32, 'INTERNAL_JWT_SECRET must be at least 32 chars'),
  INTERNAL_JWT_TTL_SEC: z.coerce.number().int().positive().default(120),

  // User Access Token verification (RS256 via JWKS from Identity).
  JWT_PUBLIC_KEY_URL: z.string().url().default('http://identity:5001/v1/.well-known/jwks.json'),
  JWT_ISSUER: z.string().default('vietride-identity'),
  JWT_AUDIENCE: z.string().default('vietride-api'),

  // Downstream service base URLs.
  IDENTITY_BASE_URL: z.string().url().default('http://localhost:5001'),
  TRIP_BASE_URL: z.string().url().default('http://localhost:5002'),
  BOOKING_BASE_URL: z.string().url().default('http://localhost:5003'),
  PAYMENT_BASE_URL: z.string().url().default('http://localhost:5004'),
  PARCEL_BASE_URL: z.string().url().default('http://localhost:5005'),
  TRACKING_BASE_URL: z.string().url().default('http://localhost:3001'),
  NOTIFICATION_BASE_URL: z.string().url().default('http://localhost:3002'),
  RAG_BASE_URL: z.string().url().default('http://localhost:3003'),

  // Redis (JWKS cache + rate limit).
  REDIS_HOST: z.string().default('localhost'),
  REDIS_PORT: z.coerce.number().int().positive().default(6379),
  REDIS_PASSWORD: z.string().optional(),

  // Rate-limit default per BACKEND_SOURCE_OF_TRUTH §11.3 — 120 req/min per IP per route.
  // Per-route overrides Day 3+.
  RATE_LIMIT_DEFAULT_PER_MIN: z.coerce.number().int().positive().default(120),

  // Deep link (Android App Links) — served on the apex domain by DeeplinkController.
  // All optional: assetlinks.json returns 404 until package + fingerprints are set.
  ANDROID_PACKAGE: z.preprocess(emptyToUndefined, z.string().optional()),
  DEEPLINK_ANDROID_PACKAGE: z.preprocess(emptyToUndefined, z.string().optional()),
  DEEPLINK_ANDROID_SHA256_FINGERPRINTS: z.preprocess(emptyToUndefined, z.string().optional()),
  DEEPLINK_APP_SCHEME: z.preprocess(emptyToUndefined, z.string().default('vietride')),
  DEEPLINK_ANDROID_STORE_URL: z.preprocess(emptyToUndefined, z.string().url().optional()),
});

export type Env = z.infer<typeof envSchema>;

export function loadEnv(raw: NodeJS.ProcessEnv = process.env): Env {
  const parsed = envSchema.safeParse(raw);
  if (!parsed.success) {
    console.error('❌ Invalid env vars:', parsed.error.flatten().fieldErrors);
    process.exit(1);
  }
  return parsed.data;
}
