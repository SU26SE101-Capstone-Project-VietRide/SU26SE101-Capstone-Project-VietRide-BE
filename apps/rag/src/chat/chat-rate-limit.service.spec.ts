import { HttpException } from '@nestjs/common';
import type { RedisService } from '@vietride/nest-redis';
import type { Env } from '../config/env.schema';
import { ChatRateLimitService } from './chat-rate-limit.service';

describe('ChatRateLimitService', () => {
  const client = {
    eval: jest.fn(),
  };
  const redis = {
    getClient: jest.fn(() => client),
  } as unknown as RedisService;

  beforeEach(() => {
    jest.clearAllMocks();
    client.eval.mockResolvedValue(1);
  });

  it('allows requests under the passenger limit', async () => {
    const service = new ChatRateLimitService(redis, makeEnv());

    await service.assertAllowed({ sub: 'user-1', role: 'PASSENGER' });

    expect(client.eval).toHaveBeenCalledWith(expect.any(String), 1, 'rag:chat:rate:user:user-1', 3600);
  });

  it('uses operator bucket for operator-scoped callers', async () => {
    const service = new ChatRateLimitService(redis, makeEnv());

    await service.assertAllowed({
      sub: 'user-1',
      role: 'OPERATOR_ADMIN',
      operatorId: 'operator-1',
    });

    expect(client.eval).toHaveBeenCalledWith(expect.any(String), 1, 'rag:chat:rate:operator:operator-1', 3600);
  });

  it('throws 429 when limit is exceeded', async () => {
    client.eval.mockResolvedValue(21);
    const service = new ChatRateLimitService(redis, makeEnv());

    await expect(service.assertAllowed({ sub: 'user-1', role: 'PASSENGER' })).rejects.toBeInstanceOf(
      HttpException,
    );
    await service.assertAllowed({ sub: 'user-1', role: 'PASSENGER' }).catch((error: unknown) => {
      expect(error).toBeInstanceOf(HttpException);
      expect((error as HttpException).getStatus()).toBe(429);
    });
  });
});

function makeEnv(): Env {
  return {
    NODE_ENV: 'test',
    PORT: 3003,
    GATEWAY_URL: 'http://gateway:3000',
    DATABASE_URL: 'postgresql://user:pass@localhost:5432/vietride_rag',
    REDIS_URL: 'redis://localhost:6379',
    REDIS_HOST: 'localhost',
    REDIS_PORT: 6379,
    RABBITMQ_URL: 'amqp://guest:guest@localhost:5672',
    RABBITMQ_EXCHANGE: 'vietride.events',
    INTERNAL_JWT_SECRET: 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa',
    INTERNAL_JWT_TTL_SEC: 120,
    JWT_ISSUER: 'vietride-identity',
    JWT_AUDIENCE: 'vietride-api',
    LOG_LEVEL: 'info',
    OPENROUTER_API_KEY: 'test-key',
    OPENROUTER_BASE_URL: 'https://openrouter.ai/api/v1',
    OPENROUTER_CHAT_MODEL: 'nex-agi/nex-n2-pro:free',
    OPENROUTER_EMBEDDING_MODEL: 'nvidia/llama-nemotron-embed-vl-1b-v2:free',
    OPENROUTER_HTTP_REFERER: undefined,
    OPENROUTER_APP_TITLE: 'VietRide RAG',
    OPENROUTER_ALLOW_PAID_FALLBACK: false,
    RAG_EMBEDDING_DIMENSIONS: 'auto',
    RAG_PROVIDER_TIMEOUT_MS: 10_000,
    RAG_MAX_MESSAGE_CHARS: 500,
    RAG_MAX_CONTEXT_TOKENS: 4_000,
    RAG_MAX_RETRIEVED_CHUNKS: 5,
    RAG_USER_RATE_LIMIT_PER_HOUR: 20,
    RAG_OPERATOR_RATE_LIMIT_PER_HOUR: 200,
    RAG_INGEST_WORKER_ENABLED: false,
    RAG_OUTBOX_PUBLISH_ENABLED: false,
    INTENT_FILTER_ENABLED: false,
    QUERY_REWRITE_ENABLED: false,
    HYBRID_SEARCH_ENABLED: false,
    RERANK_ENABLED: false,
    SUMMARIZE_ENABLED: false,
    CLOUDINARY_CLOUD_NAME: 'cloud',
    CLOUDINARY_API_KEY: 'cloud-key',
    CLOUDINARY_API_SECRET: 'cloud-secret',
    CLOUDINARY_RAG_FOLDER: 'rag/documents',
  };
}
