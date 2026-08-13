import { envSchema } from './env.schema';

describe('env.schema', () => {
  it('defaults ShopAIKey models and base URL', () => {
    const env = envSchema.parse(makeValidEnv());
    expect(env.SHOPAIKEY_BASE_URL).toBe('https://api.shopaikey.com/v1');
    expect(env.SHOPAIKEY_CHAT_MODEL).toBe('gemini-3.5-flash');
    expect(env.SHOPAIKEY_EMBEDDING_MODEL).toBe('gemini-embedding-2-preview');
  });

  it('parses ShopAIKey model overrides', () => {
    const env = envSchema.parse({
      ...makeValidEnv(),
      SHOPAIKEY_CHAT_MODEL: 'custom-chat-model',
      SHOPAIKEY_EMBEDDING_MODEL: 'custom-embedding-model',
    });
    expect(env.SHOPAIKEY_CHAT_MODEL).toBe('custom-chat-model');
    expect(env.SHOPAIKEY_EMBEDDING_MODEL).toBe('custom-embedding-model');
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
    expect(env.RAG_PROVIDER_TIMEOUT_MS).toBe(30_000);
  });
});

function makeValidEnv(): Record<string, string> {
  return {
    DATABASE_URL: 'postgresql://user:pass@localhost:5432/rag',
    REDIS_URL: 'redis://localhost:6379',
    RABBITMQ_URL: 'amqp://guest:guest@localhost:5672',
    SHOPAIKEY_API_KEY: 'test-key',
    CLOUDINARY_CLOUD_NAME: 'cloud',
    CLOUDINARY_API_KEY: 'ckey',
    CLOUDINARY_API_SECRET: 'csec',
  };
}
