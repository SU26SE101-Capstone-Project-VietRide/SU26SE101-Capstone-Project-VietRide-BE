import { ServiceUnavailableException } from '@nestjs/common';
import type { RedisService } from '@vietride/nest-redis';
import type { ChannelModel } from 'amqplib';
import type { Env } from '../config/env.schema';
import type { EmbeddingDimensionProbeService } from '../embedding/embedding-dimension-probe.service';
import type { RagPrismaService } from '../prisma/rag-prisma.service';
import { ReadinessService } from './readiness.service';

describe('ReadinessService', () => {
  const prisma = {
    $queryRaw: jest.fn(),
  } as unknown as jest.Mocked<RagPrismaService>;
  const redisClient = {
    ping: jest.fn(),
  };
  const redis = {
    getClient: jest.fn(() => redisClient),
  } as unknown as RedisService;
  const channel = {
    close: jest.fn(),
  };
  const rabbit = {
    createChannel: jest.fn(),
  } as unknown as jest.Mocked<ChannelModel>;
  const embeddingProbe = {
    probe: jest.fn(),
  } as unknown as jest.Mocked<EmbeddingDimensionProbeService>;

  beforeEach(() => {
    jest.clearAllMocks();
    prisma.$queryRaw.mockResolvedValue([{ '?column?': 1 }]);
    redisClient.ping.mockResolvedValue('PONG');
    rabbit.createChannel.mockResolvedValue(channel as never);
    channel.close.mockResolvedValue(undefined);
    embeddingProbe.probe.mockResolvedValue(2048);
  });

  it('returns ok when all dependencies are ready', async () => {
    const service = new ReadinessService(prisma, redis, embeddingProbe, makeEnv(), rabbit);

    await expect(service.check()).resolves.toEqual({
      status: 'ok',
      service: 'rag',
      dependencies: {
        prisma: 'ok',
        redis: 'ok',
        rabbitmq: 'ok',
        cloudinary: 'ok',
        openrouter: 'ok',
      },
    });
  });

  it('returns controlled 503 when a dependency fails', async () => {
    embeddingProbe.probe.mockRejectedValue(new Error('provider down'));
    const service = new ReadinessService(prisma, redis, embeddingProbe, makeEnv(), rabbit);

    await expect(service.check()).rejects.toBeInstanceOf(ServiceUnavailableException);
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
    OPENROUTER_CHAT_MODEL: 'openai/gpt-oss-120b:free',
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
