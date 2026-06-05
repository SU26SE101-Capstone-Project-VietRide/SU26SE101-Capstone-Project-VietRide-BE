import { baseEnvSchema } from '@vietride/nest-config';
import { z } from 'zod';

const booleanEnvSchema = z.preprocess((value) => {
  if (value === 'true') return true;
  if (value === 'false') return false;
  return value;
}, z.boolean());

export const envSchema = baseEnvSchema.merge(
  z.object({
    PORT: z.coerce.number().int().positive().default(3001),
    DATABASE_URL: z.string().url(),
    REDIS_URL: z.string().url(),
    RABBITMQ_URL: z.string().url(),
    RABBITMQ_EXCHANGE: z.string().default('vietride.events'),
    JWT_PUBLIC_KEY_URL: z.string().url().default('http://identity:5001/v1/.well-known/jwks.json'),
    USER_JWT_PUBLIC_KEY: z.string().optional(),
    TRIP_SERVICE_BASE_URL: z.string().url().default('http://trip:5002'),
    BOOKING_SERVICE_BASE_URL: z.string().url().default('http://booking:5003'),
    PARCEL_SERVICE_BASE_URL: z.string().url().default('http://parcel:5005'),
    TRIP_TRACKING_AUTH_PATH: z.string().default('/internal/trips/:tripId/tracking-authorization'),
    BOOKING_TRACKING_AUTH_PATH: z.string().default('/internal/trips/:tripId/tracking-authorization/bookings'),
    PARCEL_TRACKING_AUTH_PATH: z.string().default('/internal/trips/:tripId/tracking-authorization/parcels'),
    TRACKING_AUTH_HTTP_TIMEOUT_MS: z.coerce.number().int().positive().default(2_000),
    TRACKING_GPS_FLUSH_ENABLED: booleanEnvSchema.default(false),
    TRACKING_GPS_FLUSH_INTERVAL_MS: z.coerce.number().int().positive().default(300_000),
    TRACKING_TRIP_DELAY_ENABLED: booleanEnvSchema.default(false),
    TRACKING_TRIP_DELAY_INTERVAL_MS: z.coerce.number().int().positive().default(300_000),
    TRACKING_OUTBOX_PUBLISH_ENABLED: booleanEnvSchema.default(false),
    TRACKING_OUTBOX_PUBLISH_INTERVAL_MS: z.coerce.number().int().positive().default(5_000),
    TRACKING_OUTBOX_PUBLISH_BATCH_SIZE: z.coerce.number().int().positive().max(100).default(25),
  }),
);

export type Env = z.infer<typeof envSchema>;

export function loadEnv(raw: NodeJS.ProcessEnv = process.env): Env {
  const normalizedRaw = {
    ...raw,
    SENTRY_DSN: raw.SENTRY_DSN === '' ? undefined : raw.SENTRY_DSN,
    INTERNAL_JWT_SECRET: raw.INTERNAL_JWT_SECRET === '' ? undefined : raw.INTERNAL_JWT_SECRET,
    USER_JWT_PUBLIC_KEY: raw.USER_JWT_PUBLIC_KEY === '' ? undefined : raw.USER_JWT_PUBLIC_KEY,
  };
  const postgresHost = raw.POSTGRES_HOST;
  const postgresPort = raw.POSTGRES_PORT;
  const postgresUser = raw.POSTGRES_USER;
  const postgresPassword = raw.POSTGRES_PASSWORD;
  const trackingDb = raw.TRACKING_DB;
  const redisHost = raw.REDIS_HOST ?? 'localhost';
  const redisPort = raw.REDIS_PORT ?? '6379';
  const rabbitHost = raw.RABBITMQ_HOST;
  const rabbitPort = raw.RABBITMQ_PORT;
  const rabbitUser = raw.RABBITMQ_USER;
  const rabbitPassword = raw.RABBITMQ_PASSWORD;
  const databaseUrl =
    raw.DATABASE_URL ??
    (postgresHost && postgresPort && postgresUser && postgresPassword && trackingDb
      ? `postgresql://${postgresUser}:${postgresPassword}@${postgresHost}:${postgresPort}/${trackingDb}`
      : undefined);
  const rabbitMqUrl =
    raw.RABBITMQ_URL ??
    (rabbitHost && rabbitPort && rabbitUser && rabbitPassword
      ? `amqp://${rabbitUser}:${rabbitPassword}@${rabbitHost}:${rabbitPort}`
      : undefined);
  if (databaseUrl) process.env.DATABASE_URL = databaseUrl;
  process.env.REDIS_URL = raw.REDIS_URL ?? `redis://${redisHost}:${redisPort}`;
  if (rabbitMqUrl) process.env.RABBITMQ_URL = rabbitMqUrl;

  return envSchema.parse({
    ...normalizedRaw,
    DATABASE_URL: databaseUrl,
    REDIS_URL: raw.REDIS_URL ?? `redis://${redisHost}:${redisPort}`,
    RABBITMQ_URL: rabbitMqUrl,
  });
}
