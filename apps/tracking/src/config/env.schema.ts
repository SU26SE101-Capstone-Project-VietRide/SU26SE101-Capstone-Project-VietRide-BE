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
    TRACKING_DATABASE_URL: z.string().url().optional(),
    DATABASE_URL: z.string().url(),
    REDIS_URL: z.string().url(),
    RABBITMQ_URL: z.string().url(),
    RABBITMQ_EXCHANGE: z.string().default('vietride.events'),
    JWT_PUBLIC_KEY_URL: z.string().url().default('http://identity:5001/v1/.well-known/jwks.json'),
    USER_JWT_PUBLIC_KEY: z.string().optional(),
    TRIP_SERVICE_BASE_URL: z.string().url().default('http://trip:5002'),
    BOOKING_SERVICE_BASE_URL: z.string().url().default('http://booking:5003'),
    PARCEL_SERVICE_BASE_URL: z.string().url().default('http://parcel:5005'),
    TRIP_TRACKING_AUTH_PATH: z.string().default('/internal/v1/trips/:tripId/tracking-authorization'),
    BOOKING_TRACKING_AUTH_PATH: z.string().default('/internal/v1/trips/:tripId/tracking-authorization/bookings'),
    PARCEL_TRACKING_AUTH_PATH: z.string().default('/internal/v1/trips/:tripId/tracking-authorization/parcels'),
    TRIP_ROUTE_STOPS_PATH: z.string().default('/internal/v1/trips/:tripId/route-stops'),
    TRIP_ROUTE_GEOMETRY_PATH: z.string().default('/internal/v1/trips/:tripId/route-geometry'),
    BOOKING_PICKUP_BOOKINGS_PATH: z.string().default('/internal/v1/trips/:tripId/stops/:stopId/pickup-bookings'),
    TRACKING_AUTH_HTTP_TIMEOUT_MS: z.coerce.number().int().positive().default(2_000),
    TRACKING_DATA_PROVIDER_TIMEOUT_MS: z.coerce.number().int().positive().default(2_000),
    TRACKING_ROUTE_STOPS_CACHE_TTL_SECONDS: z.coerce.number().int().positive().default(60),
    TRACKING_ROUTE_GEOMETRY_CACHE_TTL_SECONDS: z.coerce.number().int().positive().default(600),
    TRACKING_SHARE_TOKEN_SECRET: z
      .string()
      .min(32, 'TRACKING_SHARE_TOKEN_SECRET must be at least 32 characters'),
    TRACKING_SHARE_PAGE_URL: z.string().url(),
    TRACKING_SHARE_TOKEN_TTL_SECONDS: z.coerce.number().int().positive().default(86_400),
    TRACKING_SHARE_CONTEXT_RATE_LIMIT_PER_MIN: z.coerce.number().int().positive().default(60),
    TRACKING_SHARE_SOCKET_RATE_LIMIT_PER_MIN: z.coerce.number().int().positive().default(20),
    TRACKING_SHARE_SOCKET_REVALIDATE_SECONDS: z.coerce.number().int().positive().default(60),
    GOOGLE_ROUTES_ENABLED: booleanEnvSchema.default(false),
    GOOGLE_ROUTES_API_KEY: z.string().trim().default(''),
    GOOGLE_ROUTES_BASE_URL: z.string().url().default('https://routes.googleapis.com'),
    TRACKING_GOOGLE_ROUTES_TIMEOUT_MS: z.coerce.number().int().positive().default(1_500),
    TRACKING_ETA_MIN_INTERVAL_SECONDS: z.coerce.number().int().min(60).default(60),
    TRACKING_ETA_CACHE_TTL_SECONDS: z.coerce.number().int().min(60).default(60),
    TRACKING_ETA_FAILURE_COOLDOWN_SECONDS: z.coerce.number().int().min(300).default(300),
    TRACKING_GPS_FLUSH_ENABLED: booleanEnvSchema.default(false),
    TRACKING_GPS_FLUSH_INTERVAL_MS: z.coerce.number().int().positive().default(300_000),
    TRACKING_TRIP_DELAY_ENABLED: booleanEnvSchema.default(false),
    TRACKING_TRIP_DELAY_INTERVAL_MS: z.coerce.number().int().positive().default(300_000),
    TRACKING_OUTBOX_PUBLISH_ENABLED: booleanEnvSchema.default(false),
    TRACKING_OUTBOX_PUBLISH_INTERVAL_MS: z.coerce.number().int().positive().default(5_000),
    TRACKING_OUTBOX_PUBLISH_BATCH_SIZE: z.coerce.number().int().positive().max(100).default(25),
    TRACKING_CORS_ORIGIN: z.string().default('*'),
    TRACKING_SWAGGER_ENABLED: booleanEnvSchema.default(true),
  }),
);

export type Env = z.infer<typeof envSchema>;

export function loadEnv(raw: NodeJS.ProcessEnv = process.env): Env {
  const tripServiceBaseUrl = raw.TRIP_SERVICE_BASE_URL || raw.TRIP_BASE_URL;
  const bookingServiceBaseUrl = raw.BOOKING_SERVICE_BASE_URL || raw.BOOKING_BASE_URL;
  const parcelServiceBaseUrl = raw.PARCEL_SERVICE_BASE_URL || raw.PARCEL_BASE_URL;
  const normalizedRaw = {
    ...raw,
    SENTRY_DSN: raw.SENTRY_DSN === '' ? undefined : raw.SENTRY_DSN,
    INTERNAL_JWT_SECRET: raw.INTERNAL_JWT_SECRET === '' ? undefined : raw.INTERNAL_JWT_SECRET,
    TRACKING_DATABASE_URL: raw.TRACKING_DATABASE_URL === '' ? undefined : raw.TRACKING_DATABASE_URL,
    USER_JWT_PUBLIC_KEY: raw.USER_JWT_PUBLIC_KEY === '' ? undefined : raw.USER_JWT_PUBLIC_KEY,
    TRIP_SERVICE_BASE_URL: tripServiceBaseUrl === '' ? undefined : tripServiceBaseUrl,
    BOOKING_SERVICE_BASE_URL: bookingServiceBaseUrl === '' ? undefined : bookingServiceBaseUrl,
    PARCEL_SERVICE_BASE_URL: parcelServiceBaseUrl === '' ? undefined : parcelServiceBaseUrl,
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
    raw.TRACKING_DATABASE_URL ??
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
  if (databaseUrl) process.env.TRACKING_DATABASE_URL = databaseUrl;
  process.env.REDIS_URL = raw.REDIS_URL ?? `redis://${redisHost}:${redisPort}`;
  if (rabbitMqUrl) process.env.RABBITMQ_URL = rabbitMqUrl;

  const parsed = envSchema.parse({
    ...normalizedRaw,
    DATABASE_URL: databaseUrl,
    REDIS_URL: raw.REDIS_URL ?? `redis://${redisHost}:${redisPort}`,
    RABBITMQ_URL: rabbitMqUrl,
  });
  if (parsed.GOOGLE_ROUTES_ENABLED && parsed.GOOGLE_ROUTES_API_KEY.length === 0) {
    throw new Error('GOOGLE_ROUTES_API_KEY is required when GOOGLE_ROUTES_ENABLED=true');
  }
  return parsed;
}
