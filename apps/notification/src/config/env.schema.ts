import { baseEnvSchema } from '@vietride/nest-config';
import { z } from 'zod';

export const envSchema = baseEnvSchema.merge(
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
    FCM_PROJECT_ID: z.string().optional(),
    FCM_CLIENT_EMAIL: z.string().email().optional(),
    FCM_PRIVATE_KEY: z.string().optional(),
    SENDGRID_API_KEY: z.string().optional(),
    SENDGRID_FROM_EMAIL: z.string().email().optional(),
    SENDGRID_FROM_NAME: z.string().default('VietRide'),
    NOTIFICATION_RETENTION_DAYS: z.coerce.number().int().positive().default(90),
    NOTIFICATION_RETENTION_JOB_INTERVAL_MS: z.coerce.number().int().positive().default(86_400_000),
  }),
);

export type Env = z.infer<typeof envSchema>;

export function loadEnv(raw: NodeJS.ProcessEnv = process.env): Env {
  const normalizedRaw = {
    ...raw,
    SENTRY_DSN: raw.SENTRY_DSN === '' ? undefined : raw.SENTRY_DSN,
    INTERNAL_JWT_SECRET: raw.INTERNAL_JWT_SECRET === '' ? undefined : raw.INTERNAL_JWT_SECRET,
    NOTIFICATION_DATABASE_URL: raw.NOTIFICATION_DATABASE_URL === '' ? undefined : raw.NOTIFICATION_DATABASE_URL,
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
    DATABASE_URL: databaseUrl,
    REDIS_URL: redisUrl,
    RABBITMQ_URL: rabbitMqUrl,
  });
}
