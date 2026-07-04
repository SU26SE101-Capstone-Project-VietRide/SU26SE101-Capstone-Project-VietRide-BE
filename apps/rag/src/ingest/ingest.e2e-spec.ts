import { Test, TestingModule } from '@nestjs/testing';
import { ENV_TOKEN, EMBEDDING_PROVIDER, STORAGE_PROVIDER } from '../app/tokens';
import type { Env } from '../config/env.schema';
import type { KnowledgeDocument } from '../generated/rag-prisma-client';
import type { EmbeddingProvider } from '../providers/embedding.provider';
import type { StorageProvider } from '../providers/storage.provider';
import { IngestIdempotencyService } from './ingest-idempotency.service';
import { IngestRepository } from './ingest.repository';
import { IngestService } from './ingest.service';

const DOCUMENT_ID = '33333333-3333-3333-3333-333333333333';
const EVENT_ID = '44444444-4444-4444-4444-444444444444';
const EMBEDDING_DIMENSIONS = 2_048;

describe('IngestService (e2e)', () => {
  it('processes a pending ingest event end-to-end through mocked infrastructure', async () => {
    const repository = {
      findPendingEvents: jest.fn().mockResolvedValue([makeOutboxEvent()]),
      markEventPublishing: jest.fn().mockResolvedValue(true),
      markEventPublished: jest.fn(),
      markEventFailed: jest.fn(),
      findDocumentForIngest: jest.fn().mockResolvedValue(makeDocument()),
      markDocumentProcessing: jest.fn().mockResolvedValue(true),
      replaceChunksAndComplete: jest.fn(),
      markDocumentFailed: jest.fn(),
      countChunks: jest.fn(),
    };
    const idempotency = {
      begin: jest.fn().mockResolvedValue('acquired'),
      markProcessed: jest.fn(),
      release: jest.fn(),
    };
    const storageProvider: jest.Mocked<StorageProvider> = {
      uploadObject: jest.fn(),
      downloadObject: jest
        .fn()
        .mockResolvedValue(Buffer.from('# Hỗ trợ\nNội dung hỗ trợ hành khách')),
      createSignedReadUrl: jest.fn(),
    };
    const embeddingProvider: jest.Mocked<EmbeddingProvider> = {
      embed: jest.fn().mockResolvedValue(makeEmbedding()),
    };

    const moduleFixture: TestingModule = await Test.createTestingModule({
      providers: [
        IngestService,
        { provide: IngestRepository, useValue: repository },
        { provide: IngestIdempotencyService, useValue: idempotency },
        { provide: STORAGE_PROVIDER, useValue: storageProvider },
        { provide: EMBEDDING_PROVIDER, useValue: embeddingProvider },
        { provide: ENV_TOKEN, useValue: makeEnv() },
      ],
    }).compile();

    const service = moduleFixture.get(IngestService);

    await expect(service.processPendingOnce(1)).resolves.toBe(1);

    expect(repository.replaceChunksAndComplete).toHaveBeenCalledWith(
      expect.objectContaining({ id: '33333333-3333-3333-3333-333333333333' }),
      expect.arrayContaining([expect.objectContaining({ content: 'Nội dung hỗ trợ hành khách' })]),
      'nvidia/llama-nemotron-embed-vl-1b-v2:free',
      EMBEDDING_DIMENSIONS,
    );
    expect(repository.markEventPublished).toHaveBeenCalledWith(EVENT_ID);
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
    OPENROUTER_CHAT_MODEL: 'nvidia/nemotron-3-ultra-550b-a55b:free',
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

function makeEmbedding(): number[] {
  return Array.from({ length: EMBEDDING_DIMENSIONS }, (_, index) => index / EMBEDDING_DIMENSIONS);
}

function makeDocument(overrides: Partial<KnowledgeDocument> = {}): KnowledgeDocument {
  return {
    id: DOCUMENT_ID,
    title: 'FAQ hành khách',
    description: null,
    storageProvider: 'CLOUDINARY',
    storagePath: 'documents/faq.txt',
    fileName: 'faq.txt',
    mimeType: 'text/plain',
    fileSize: BigInt(42),
    fileType: 'TXT',
    accessLevel: 'PUBLIC',
    category: 'CUSTOMER_SUPPORT',
    documentType: 'FAQ',
    audienceRoles: ['PASSENGER'],
    language: 'vi',
    operatorId: null,
    status: 'APPROVED',
    ingestStatus: 'PENDING',
    ingestError: null,
    ingestedAt: null,
    chunkCount: null,
    embeddingModel: null,
    embeddingDimensions: null,
    uploadedByUserId: '11111111-1111-1111-1111-111111111111',
    approvedByUserId: '11111111-1111-1111-1111-111111111111',
    approvedAt: new Date('2026-06-13T00:00:00.000Z'),
    archivedAt: null,
    createdAt: new Date('2026-06-13T00:00:00.000Z'),
    updatedAt: new Date('2026-06-13T00:00:00.000Z'),
    ...overrides,
  };
}

function makeOutboxEvent() {
  return {
    id: EVENT_ID,
    eventType: 'rag.document.ingest_requested',
    payload: {
      documentId: DOCUMENT_ID,
      storagePath: 'documents/faq.txt',
      fileType: 'TXT',
      accessLevel: 'PUBLIC',
      operatorId: null,
    },
    status: 'PENDING',
    retryCount: 0,
    lastError: null,
    createdAt: new Date('2026-06-13T00:00:00.000Z'),
    publishedAt: null,
  } as const;
}
