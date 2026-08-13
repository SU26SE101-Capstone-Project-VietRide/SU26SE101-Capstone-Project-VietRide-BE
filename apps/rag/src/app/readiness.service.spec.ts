import { ServiceUnavailableException } from '@nestjs/common';
import { RabbitMqTopologyHealth } from '@vietride/nest-rabbitmq';
import type { RedisService } from '@vietride/nest-redis';
import type { ChannelModel } from 'amqplib';
import type { Env } from '../config/env.schema';
import type { EmbeddingDimensionProbeService } from '../embedding/embedding-dimension-probe.service';
import type { RagPrismaService } from '../prisma/rag-prisma.service';
import type { ChatCompletionProvider } from '../providers/chat-completion.provider';
import { ReadinessService } from './readiness.service';

describe('ReadinessService', () => {
  const knowledgeDocumentCount = jest.fn();
  const prisma = {
    $queryRaw: jest.fn(),
    knowledgeDocument: {
      count: knowledgeDocumentCount,
    },
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
  const chatProvider = {
    complete: jest.fn(),
    stream: jest.fn(),
  } as unknown as jest.Mocked<ChatCompletionProvider>;

  let topologyHealth: RabbitMqTopologyHealth;

  beforeEach(() => {
    jest.clearAllMocks();
    topologyHealth = new RabbitMqTopologyHealth();
    prisma.$queryRaw.mockResolvedValue([{ '?column?': 1 }]);
    redisClient.ping.mockResolvedValue('PONG');
    rabbit.createChannel.mockResolvedValue(channel as never);
    channel.close.mockResolvedValue(undefined);
    embeddingProbe.probe.mockResolvedValue(2048);
    chatProvider.complete.mockResolvedValue('ok');
    knowledgeDocumentCount.mockResolvedValue(0);
  });

  it('returns ok when all dependencies are ready', async () => {
    const service = new ReadinessService(
      prisma,
      redis,
      embeddingProbe,
      chatProvider,
      makeEnv(),
      rabbit,
      topologyHealth,
    );

    await expect(service.check()).resolves.toEqual({
      status: 'ok',
      service: 'rag',
      dependencies: {
        prisma: 'ok',
        redis: 'ok',
        rabbitmq: 'ok',
        cloudinary: 'ok',
        shopaikey: 'ok',
        ingest: 'ok',
      },
    });
  });

  it('returns controlled 503 when a dependency fails', async () => {
    embeddingProbe.probe.mockRejectedValue(new Error('provider down'));
    const service = new ReadinessService(
      prisma,
      redis,
      embeddingProbe,
      chatProvider,
      makeEnv(),
      rabbit,
      topologyHealth,
    );

    await expect(service.check()).rejects.toBeInstanceOf(ServiceUnavailableException);
  });

  it('fails readiness when the chat provider probe fails', async () => {
    chatProvider.complete.mockRejectedValue(new Error('chat provider down'));
    const service = new ReadinessService(
      prisma,
      redis,
      embeddingProbe,
      chatProvider,
      makeEnv(),
      rabbit,
      topologyHealth,
    );

    await expect(service.check()).rejects.toBeInstanceOf(ServiceUnavailableException);
  });

  it('fails readiness when the ingest worker is disabled', async () => {
    const service = new ReadinessService(
      prisma,
      redis,
      embeddingProbe,
      chatProvider,
      makeEnv({ RAG_INGEST_WORKER_ENABLED: false }),
      rabbit,
      topologyHealth,
    );

    await expect(service.check()).rejects.toBeInstanceOf(ServiceUnavailableException);
  });

  it('fails readiness when stale ingest work is backlogged', async () => {
    knowledgeDocumentCount.mockResolvedValue(1);
    const service = new ReadinessService(
      prisma,
      redis,
      embeddingProbe,
      chatProvider,
      makeEnv(),
      rabbit,
      topologyHealth,
    );

    await expect(service.check()).rejects.toBeInstanceOf(ServiceUnavailableException);
  });

  it('fails readiness and names the queue when a consumer failed topology assertion', async () => {
    topologyHealth.record({
      queue: 'rag:document-ingested',
      routingKey: 'rag.document.ingested',
      error: "PRECONDITION_FAILED - inequivalent arg 'x-dead-letter-routing-key'",
    });
    const service = new ReadinessService(
      prisma,
      redis,
      embeddingProbe,
      chatProvider,
      makeEnv(),
      rabbit,
      topologyHealth,
    );

    await expect(service.check()).rejects.toMatchObject({
      response: expect.objectContaining({
        errorCode: 'RAG_DEPENDENCY_UNAVAILABLE',
        detail: '1 RabbitMQ consumer(s) failed topology assertion',
        failedConsumers: [{ queue: 'rag:document-ingested', routingKey: 'rag.document.ingested' }],
      }),
    });
  });
});

function makeEnv(overrides: Partial<Env> = {}): Env {
  return {
    NODE_ENV: 'test',
    PORT: 3003,
    GATEWAY_URL: 'http://gateway:3000',
    IDENTITY_INTERNAL_BASE_URL: 'http://identity:5001',
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
    SHOPAIKEY_API_KEY: 'test-key',
    SHOPAIKEY_BASE_URL: 'https://api.shopaikey.com/v1',
    SHOPAIKEY_CHAT_MODEL: 'gemini-3.5-flash',
    SHOPAIKEY_EMBEDDING_MODEL: 'gemini-embedding-2-preview',
    RAG_PROVIDER_TIMEOUT_MS: 10_000,
    RAG_MAX_MESSAGE_CHARS: 500,
    RAG_MAX_CONTEXT_TOKENS: 4_000,
    RAG_MAX_RETRIEVED_CHUNKS: 5,
    RAG_USER_RATE_LIMIT_PER_HOUR: 20,
    RAG_OPERATOR_RATE_LIMIT_PER_HOUR: 200,
    RAG_INGEST_WORKER_ENABLED: true,
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
    ...overrides,
  };
}
