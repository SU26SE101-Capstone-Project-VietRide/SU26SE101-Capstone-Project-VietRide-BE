import { envSchema } from './env.schema';

describe('env.schema', () => {
  it('defaults OPENROUTER_CHAT_MODEL to nvidia/nemotron-3-ultra-550b-a55b:free', () => {
    const env = envSchema.parse(makeValidEnv());
    expect(env.OPENROUTER_CHAT_MODEL).toBe('nvidia/nemotron-3-ultra-550b-a55b:free');
  });

  it('parses OPENROUTER_CHAT_MODEL override', () => {
    const env = envSchema.parse({
      ...makeValidEnv(),
      OPENROUTER_CHAT_MODEL: 'custom/model:free',
    });
    expect(env.OPENROUTER_CHAT_MODEL).toBe('custom/model:free');
  });

  it('enables the complete free-only RAG feature set by default', () => {
    const env = envSchema.parse(makeValidEnv());

    expect({
      ingest: env.RAG_INGEST_WORKER_ENABLED,
      intent: env.INTENT_FILTER_ENABLED,
      rewrite: env.QUERY_REWRITE_ENABLED,
      hybrid: env.HYBRID_SEARCH_ENABLED,
      rerank: env.RERANK_ENABLED,
      summarize: env.SUMMARIZE_ENABLED,
    }).toEqual({
      ingest: true,
      intent: true,
      rewrite: true,
      hybrid: true,
      rerank: true,
      summarize: true,
    });
    expect(env.RAG_OUTBOX_PUBLISH_ENABLED).toBe(false);
    expect(env.OPENROUTER_ALLOW_PAID_FALLBACK).toBe(false);
    expect(env.RAG_PROVIDER_TIMEOUT_MS).toBe(30_000);
  });
});

function makeValidEnv(): Record<string, string> {
  return {
    DATABASE_URL: 'postgresql://user:pass@localhost:5432/rag',
    REDIS_URL: 'redis://localhost:6379',
    RABBITMQ_URL: 'amqp://guest:guest@localhost:5672',
    OPENROUTER_API_KEY: 'test-key',
    CLOUDINARY_CLOUD_NAME: 'cloud',
    CLOUDINARY_API_KEY: 'ckey',
    CLOUDINARY_API_SECRET: 'csec',
  };
}
