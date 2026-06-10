import { baseEnvSchema } from '@vietride/nest-config';
import { z } from 'zod';

export const envSchema = baseEnvSchema.merge(
  z.object({
    PORT: z.coerce.number().int().positive().default(3002),
    REDIS_URL: z.string().url(),
    RABBITMQ_URL: z.string().url(),
    RABBITMQ_EXCHANGE: z.string().default('vietride.events'),
  }),
);

export type Env = z.infer<typeof envSchema>;

export function loadEnv(raw: NodeJS.ProcessEnv = process.env): Env {
  const normalizedRaw = {
    ...raw,
    SENTRY_DSN: raw.SENTRY_DSN === '' ? undefined : raw.SENTRY_DSN,
    INTERNAL_JWT_SECRET: raw.INTERNAL_JWT_SECRET === '' ? undefined : raw.INTERNAL_JWT_SECRET,
  };
  const redisHost = raw.REDIS_HOST ?? 'localhost';
  const redisPort = raw.REDIS_PORT ?? '6379';
  const rabbitHost = raw.RABBITMQ_HOST;
  const rabbitPort = raw.RABBITMQ_PORT;
  const rabbitUser = raw.RABBITMQ_USER;
  const rabbitPassword = raw.RABBITMQ_PASSWORD;
  const redisUrl = raw.REDIS_URL ?? `redis://${redisHost}:${redisPort}`;
  const rabbitMqUrl =
    raw.RABBITMQ_URL ??
    (rabbitHost && rabbitPort && rabbitUser && rabbitPassword
      ? `amqp://${rabbitUser}:${rabbitPassword}@${rabbitHost}:${rabbitPort}`
      : undefined);
  process.env.REDIS_URL = redisUrl;
  if (rabbitMqUrl) process.env.RABBITMQ_URL = rabbitMqUrl;

  return envSchema.parse({
    ...normalizedRaw,
    REDIS_URL: redisUrl,
    RABBITMQ_URL: rabbitMqUrl,
  });
}
