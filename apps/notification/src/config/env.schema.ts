import { baseEnvSchema } from '@vietride/nest-config';
import { z } from 'zod';

const envBooleanSchema = z.preprocess((value) => {
  if (typeof value !== 'string') return value;

  const normalized = value.trim().toLowerCase();
  if (normalized === 'true') return true;
  if (normalized === 'false') return false;
  return value;
}, z.boolean());

export const envSchema = baseEnvSchema
  .merge(
    z.object({
      PORT: z.coerce.number().int().positive().default(3002),
      NOTIFICATION_DATABASE_URL: z.string().url().optional(),
      DATABASE_URL: z.string().url(),
      REDIS_URL: z.string().url(),
      RABBITMQ_URL: z.string().url(),
      RABBITMQ_EXCHANGE: z.string().default('vietride.events'),
      JWT_PUBLIC_KEY_URL: z.string().url().default('http://identity:5001/v1/.well-known/jwks.json'),
      USER_JWT_PUBLIC_KEY: z.string().optional(),
      IDENTITY_INTERNAL_BASE_URL: z.string().url().default('http://identity:5001'),
      BOOKING_INTERNAL_BASE_URL: z.string().url().default('http://booking:5003'),
      PARCEL_INTERNAL_BASE_URL: z.string().url().default('http://parcel:5005'),
      FCM_PROJECT_ID: z.string().optional(),
      TRIP_INTERNAL_BASE_URL: z.string().url().default('http://trip:5002'),
      FCM_CLIENT_EMAIL: z.string().email().optional(),
      FCM_PRIVATE_KEY: z.string().optional(),
      SENDGRID_API_KEY: z.string().optional(),
      FCM_DRY_RUN: envBooleanSchema.default(false),
      FCM_DRY_RUN_TOPIC: z.string().trim().min(1).default('vietride-e2e-validation'),
      SENDGRID_FROM_EMAIL: z.string().email().optional(),
      SENDGRID_FROM_NAME: z.string().default('VietRide'),
      NOTIFICATION_RETENTION_DAYS: z.coerce.number().int().positive().default(90),
      NOTIFICATION_RETENTION_JOB_INTERVAL_MS: z.coerce
        .number()
        .int()
        .positive()
        .default(86_400_000),
    }),
  )
  .superRefine((env, ctx) => {
    if (env.NODE_ENV !== 'production') return;

    if (!env.SENDGRID_API_KEY) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['SENDGRID_API_KEY'],
        message: 'SENDGRID_API_KEY is required in production',
      });
    }

    if (!env.SENDGRID_FROM_EMAIL) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['SENDGRID_FROM_EMAIL'],
        message: 'SENDGRID_FROM_EMAIL is required in production',
      });
    }
    if (!env.FCM_PROJECT_ID || !env.FCM_CLIENT_EMAIL || !env.FCM_PRIVATE_KEY) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['FCM_PROJECT_ID'],
        message: 'FCM_PROJECT_ID, FCM_CLIENT_EMAIL and FCM_PRIVATE_KEY are required in production',
      });
    }

    if (env.FCM_DRY_RUN) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['FCM_DRY_RUN'],
        message: 'FCM_DRY_RUN must be false in production',
      });
    }
  });

export type Env = z.infer<typeof envSchema>;

export function loadEnv(raw: NodeJS.ProcessEnv = process.env): Env {
  const normalizedRaw = {
    ...raw,
    SENTRY_DSN: raw.SENTRY_DSN === '' ? undefined : raw.SENTRY_DSN,
    INTERNAL_JWT_SECRET: raw.INTERNAL_JWT_SECRET === '' ? undefined : raw.INTERNAL_JWT_SECRET,
    NOTIFICATION_DATABASE_URL:
      raw.NOTIFICATION_DATABASE_URL === '' ? undefined : raw.NOTIFICATION_DATABASE_URL,
    USER_JWT_PUBLIC_KEY: raw.USER_JWT_PUBLIC_KEY === '' ? undefined : raw.USER_JWT_PUBLIC_KEY,
    FCM_PROJECT_ID: raw.FCM_PROJECT_ID === '' ? undefined : raw.FCM_PROJECT_ID,
    FCM_CLIENT_EMAIL: raw.FCM_CLIENT_EMAIL === '' ? undefined : raw.FCM_CLIENT_EMAIL,
    FCM_PRIVATE_KEY: raw.FCM_PRIVATE_KEY === '' ? undefined : raw.FCM_PRIVATE_KEY,
    SENDGRID_API_KEY: raw.SENDGRID_API_KEY === '' ? undefined : raw.SENDGRID_API_KEY,
    SENDGRID_FROM_EMAIL: raw.SENDGRID_FROM_EMAIL === '' ? undefined : raw.SENDGRID_FROM_EMAIL,
  };
  const postgresHost = raw.POSTGRES_HOST;
  const postgresPort = raw.POSTGRES_PORT;
  const postgresUser = raw.POSTGRES_USER;
  const postgresPassword = raw.POSTGRES_PASSWORD;
  const notificationDb = raw.NOTIFICATION_DB;
  const redisHost = raw.REDIS_HOST ?? 'localhost';
  const redisPort = raw.REDIS_PORT ?? '6379';
  const rabbitHost = raw.RABBITMQ_HOST;
  const rabbitPort = raw.RABBITMQ_PORT;
  const rabbitUser = raw.RABBITMQ_USER;
  const rabbitPassword = raw.RABBITMQ_PASSWORD;
  const databaseUrl =
    raw.NOTIFICATION_DATABASE_URL ??
    raw.DATABASE_URL ??
    (postgresHost && postgresPort && postgresUser && postgresPassword && notificationDb
      ? `postgresql://${postgresUser}:${postgresPassword}@${postgresHost}:${postgresPort}/${notificationDb}`
      : undefined);
  const redisUrl = raw.REDIS_URL ?? `redis://${redisHost}:${redisPort}`;
  const rabbitMqUrl =
    raw.RABBITMQ_URL ??
    (rabbitHost && rabbitPort && rabbitUser && rabbitPassword
      ? `amqp://${rabbitUser}:${rabbitPassword}@${rabbitHost}:${rabbitPort}`
      : undefined);

  if (databaseUrl) process.env.DATABASE_URL = databaseUrl;
  if (databaseUrl) process.env.NOTIFICATION_DATABASE_URL = databaseUrl;
  process.env.REDIS_URL = redisUrl;
  if (rabbitMqUrl) process.env.RABBITMQ_URL = rabbitMqUrl;

  return envSchema.parse({
    ...normalizedRaw,
    BOOKING_INTERNAL_BASE_URL: raw.BOOKING_INTERNAL_BASE_URL ?? raw.BOOKING_BASE_URL,
    PARCEL_INTERNAL_BASE_URL: raw.PARCEL_INTERNAL_BASE_URL ?? raw.PARCEL_BASE_URL,
    DATABASE_URL: databaseUrl,
    REDIS_URL: redisUrl,
    RABBITMQ_URL: rabbitMqUrl,
  });
}
