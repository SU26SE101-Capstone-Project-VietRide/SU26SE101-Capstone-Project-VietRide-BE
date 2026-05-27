import { z } from 'zod';

/**
 * Base env schema shared by every VietRide NestJS service.
 * Apps should `.merge()` this with their own service-specific schema.
 */
export const baseEnvSchema = z.object({
  NODE_ENV: z.enum(['development', 'test', 'production']).default('development'),
  PORT: z.coerce.number().int().positive().default(3000),

  GATEWAY_URL: z.string().url().default('http://gateway:3000'),

  // Internal JWT (HS256) shared with .NET services.
  INTERNAL_JWT_SECRET: z.string().min(32, 'INTERNAL_JWT_SECRET must be at least 32 chars').optional(),
  INTERNAL_JWT_TTL_SEC: z.coerce.number().int().positive().default(120),

  // User Access Token verification (RS256 via JWKS from Identity).
  JWT_PUBLIC_KEY_URL: z.string().url().optional(),
  JWT_ISSUER: z.string().default('vietride-identity'),
  JWT_AUDIENCE: z.string().default('vietride-api'),

  // Redis.
  REDIS_URL: z.string().optional(),
  REDIS_HOST: z.string().default('localhost'),
  REDIS_PORT: z.coerce.number().int().positive().default(6379),
  REDIS_PASSWORD: z.string().optional(),

  // RabbitMQ.
  RABBITMQ_URL: z.string().optional(),
  RABBITMQ_EXCHANGE: z.string().default('vietride.events'),

  // Postgres.
  DATABASE_URL: z.string().optional(),

  // Observability v1 = Sentry + Serilog/Winston stdout (per BACKEND_SOURCE_OF_TRUTH §9.13).
  // Prometheus/Grafana/Tempo/Loki/OpenTelemetry deferred to v2.
  SENTRY_DSN: z.string().url().optional(),
  LOG_LEVEL: z.enum(['trace', 'debug', 'info', 'warn', 'error', 'fatal']).default('info'),
});

export type BaseEnv = z.infer<typeof baseEnvSchema>;
