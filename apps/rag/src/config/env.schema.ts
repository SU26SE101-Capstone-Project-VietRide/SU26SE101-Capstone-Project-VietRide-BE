import { baseEnvSchema } from '@vietride/nest-config';
import { z } from 'zod';

const booleanEnvSchema = z.preprocess((value) => {
  if (value === 'true') return true;
  if (value === 'false') return false;
  return value;
}, z.boolean());

const optionalNonEmptyString = z.preprocess((value) => {
  if (value === '') return undefined;
  return value;
}, z.string().optional());

export const envSchema = baseEnvSchema.merge(
  z.object({
    PORT: z.coerce.number().int().positive().default(3003),
    RAG_DATABASE_URL: z.string().url().optional(),
    DATABASE_URL: z.string().url(),
    REDIS_URL: z.string().url(),
    RABBITMQ_URL: z.string().url(),
    RABBITMQ_EXCHANGE: z.string().default('vietride.events'),
    OPENROUTER_API_KEY: z.string().min(1),
    OPENROUTER_BASE_URL: z.string().url().default('https://openrouter.ai/api/v1'),
    OPENROUTER_CHAT_MODEL: z.string().default('nex-agi/nex-n2-pro:free'),
    OPENROUTER_EMBEDDING_MODEL: z
      .string()
      .default('nvidia/llama-nemotron-embed-vl-1b-v2:free'),
    OPENROUTER_HTTP_REFERER: optionalNonEmptyString,
    OPENROUTER_APP_TITLE: z.string().default('VietRide RAG'),
    OPENROUTER_ALLOW_PAID_FALLBACK: booleanEnvSchema.default(false),
    RAG_EMBEDDING_DIMENSIONS: z
      .union([z.literal('auto'), z.coerce.number().int().positive()])
      .default('auto'),
    RAG_PROVIDER_TIMEOUT_MS: z.coerce.number().int().positive().default(10_000),
    RAG_MAX_MESSAGE_CHARS: z.coerce.number().int().positive().default(500),
    RAG_MAX_CONTEXT_TOKENS: z.coerce.number().int().positive().default(4_000),
    RAG_MAX_RETRIEVED_CHUNKS: z.coerce.number().int().positive().max(10).default(5),
    RAG_USER_RATE_LIMIT_PER_HOUR: z.coerce.number().int().positive().default(20),
    RAG_OPERATOR_RATE_LIMIT_PER_HOUR: z.coerce.number().int().positive().default(200),
    RAG_INGEST_WORKER_ENABLED: booleanEnvSchema.default(false),
    RAG_OUTBOX_PUBLISH_ENABLED: booleanEnvSchema.default(false),
    INTENT_FILTER_ENABLED: booleanEnvSchema.default(false),
    QUERY_REWRITE_ENABLED: booleanEnvSchema.default(false),
    HYBRID_SEARCH_ENABLED: booleanEnvSchema.default(false),
    RERANK_ENABLED: booleanEnvSchema.default(false),
    SUMMARIZE_ENABLED: booleanEnvSchema.default(false),
    CLOUDINARY_CLOUD_NAME: z.string().min(1),
    CLOUDINARY_API_KEY: z.string().min(1),
    CLOUDINARY_API_SECRET: z.string().min(1),
    CLOUDINARY_RAG_FOLDER: z.string().default('rag/documents'),
  }),
);

export type Env = z.infer<typeof envSchema>;

export function loadEnv(raw: NodeJS.ProcessEnv = process.env): Env {
  const normalizedRaw = {
    ...raw,
    SENTRY_DSN: raw.SENTRY_DSN === '' ? undefined : raw.SENTRY_DSN,
    INTERNAL_JWT_SECRET: raw.INTERNAL_JWT_SECRET === '' ? undefined : raw.INTERNAL_JWT_SECRET,
    RAG_DATABASE_URL: raw.RAG_DATABASE_URL === '' ? undefined : raw.RAG_DATABASE_URL,
  };
  const postgresHost = raw.POSTGRES_HOST;
  const postgresPort = raw.POSTGRES_PORT;
  const postgresUser = raw.POSTGRES_USER;
  const postgresPassword = raw.POSTGRES_PASSWORD;
  const ragDb = raw.RAG_DB ?? 'vietride_rag';
  const redisHost = raw.REDIS_HOST ?? 'localhost';
  const redisPort = raw.REDIS_PORT ?? '6379';
  const rabbitHost = raw.RABBITMQ_HOST;
  const rabbitPort = raw.RABBITMQ_PORT;
  const rabbitUser = raw.RABBITMQ_USER;
  const rabbitPassword = raw.RABBITMQ_PASSWORD;
  const databaseUrl =
    raw.RAG_DATABASE_URL ??
    raw.DATABASE_URL ??
    (postgresHost && postgresPort && postgresUser && postgresPassword
      ? `postgresql://${postgresUser}:${postgresPassword}@${postgresHost}:${postgresPort}/${ragDb}`
      : undefined);
  const redisUrl = raw.REDIS_URL ?? `redis://${redisHost}:${redisPort}`;
  const rabbitMqUrl =
    raw.RABBITMQ_URL ??
    (rabbitHost && rabbitPort && rabbitUser && rabbitPassword
      ? `amqp://${rabbitUser}:${rabbitPassword}@${rabbitHost}:${rabbitPort}`
      : undefined);

  if (databaseUrl) process.env.DATABASE_URL = databaseUrl;
  if (databaseUrl) process.env.RAG_DATABASE_URL = databaseUrl;
  process.env.REDIS_URL = redisUrl;
  if (rabbitMqUrl) process.env.RABBITMQ_URL = rabbitMqUrl;

  return envSchema.parse({
    ...normalizedRaw,
    DATABASE_URL: databaseUrl,
    REDIS_URL: redisUrl,
    RABBITMQ_URL: rabbitMqUrl,
  });
}
