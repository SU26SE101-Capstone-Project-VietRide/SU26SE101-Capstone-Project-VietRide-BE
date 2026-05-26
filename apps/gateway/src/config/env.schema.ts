import { z } from 'zod';

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
});

export type Env = z.infer<typeof envSchema>;

export function loadEnv(raw: NodeJS.ProcessEnv = process.env): Env {
  const parsed = envSchema.safeParse(raw);
  if (!parsed.success) {
    // eslint-disable-next-line no-console
    console.error('❌ Invalid env vars:', parsed.error.flatten().fieldErrors);
    process.exit(1);
  }
  return parsed.data;
}
