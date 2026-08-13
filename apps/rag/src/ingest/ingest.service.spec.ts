import { ServiceUnavailableException } from '@nestjs/common';
import type { Env } from '../config/env.schema';
import type { KnowledgeDocument } from '../generated/rag-prisma-client';
import type { EmbeddingProvider } from '../providers/embedding.provider';
import type { StorageProvider } from '../providers/storage.provider';
import { IngestIdempotencyService } from './ingest-idempotency.service';
import { IngestRepository } from './ingest.repository';
import { IngestService } from './ingest.service';
import type { RagIngestOutboxEvent } from './ingest.types';

const DOCUMENT_ID = '33333333-3333-3333-3333-333333333333';
const EVENT_ID = '44444444-4444-4444-4444-444444444444';
const EMBEDDING_DIMENSIONS = 3_072;

describe('IngestService', () => {
  let service: IngestService;
  let repository: jest.Mocked<IngestRepository>;
  let idempotency: jest.Mocked<IngestIdempotencyService>;
  let storageProvider: jest.Mocked<StorageProvider>;
  let embeddingProvider: jest.Mocked<EmbeddingProvider>;

  beforeEach(() => {
    repository = {
      findPendingEvents: jest.fn(),
      markEventPublishing: jest.fn(),
      markEventPublished: jest.fn(),
      markEventFailed: jest.fn(),
      markEventDiscarded: jest.fn(),
      findDocumentForIngest: jest.fn(),
      markDocumentProcessing: jest.fn(),
      replaceChunksAndComplete: jest.fn(),
      markDocumentFailed: jest.fn(),
      countChunks: jest.fn(),
    } as unknown as jest.Mocked<IngestRepository>;
    idempotency = {
      begin: jest.fn(),
      markProcessed: jest.fn(),
      release: jest.fn(),
    } as unknown as jest.Mocked<IngestIdempotencyService>;
    storageProvider = {
      uploadObject: jest.fn(),
      downloadObject: jest.fn(),
      createSignedReadUrl: jest.fn(),
    };
    embeddingProvider = {
      embed: jest.fn(),
    };

    service = new IngestService(
      repository,
      idempotency,
      storageProvider,
      embeddingProvider,
      makeEnv(),
    );
  });

  it('downloads approved TXT, chunks by heading, embeds, and completes document ingest', async () => {
    idempotency.begin.mockResolvedValue({ state: 'acquired', ownerToken: 'owner-1' });
    repository.findDocumentForIngest.mockResolvedValue(makeDocument());
    repository.markDocumentProcessing.mockResolvedValue(true);
    storageProvider.downloadObject.mockResolvedValue(
      Buffer.from('# Vé xe\nCách đặt vé VietRide\n\n# Hoàn tiền\nQuy trình hoàn tiền'),
    );
    embeddingProvider.embed.mockResolvedValue(makeEmbedding());

    await expect(service.processDocument(DOCUMENT_ID)).resolves.toBe(true);

    expect(repository.replaceChunksAndComplete).toHaveBeenCalledWith(
      expect.objectContaining({ id: DOCUMENT_ID }),
      expect.arrayContaining([
        expect.objectContaining({ sectionHeader: 'Vé xe', chunkIndex: 0 }),
        expect.objectContaining({ sectionHeader: 'Hoàn tiền', chunkIndex: 1 }),
      ]),
      'gemini-embedding-2-preview',
      EMBEDDING_DIMENSIONS,
    );
    expect(idempotency.markProcessed).toHaveBeenCalledWith(DOCUMENT_ID, 'owner-1');
  });

  it('skips duplicate ingest without inserting chunks', async () => {
    idempotency.begin.mockResolvedValue({ state: 'duplicate' });

    await expect(service.processDocument(DOCUMENT_ID)).resolves.toBe(false);

    expect(repository.findDocumentForIngest).not.toHaveBeenCalled();
    expect(repository.replaceChunksAndComplete).not.toHaveBeenCalled();
  });

  it('marks document failed and releases lock when provider is unavailable', async () => {
    const providerError = new ServiceUnavailableException({
      errorCode: 'RAG_PROVIDER_RATE_LIMITED',
      detail: 'ShopAIKey embedding provider rate limit reached',
    });
    idempotency.begin.mockResolvedValue({ state: 'acquired', ownerToken: 'owner-2' });
    repository.findDocumentForIngest.mockResolvedValue(makeDocument());
    repository.markDocumentProcessing.mockResolvedValue(true);
    storageProvider.downloadObject.mockResolvedValue(Buffer.from('Nội dung kiểm thử ingest'));
    embeddingProvider.embed.mockRejectedValue(providerError);

    await expect(service.processDocument(DOCUMENT_ID)).rejects.toBe(providerError);

    expect(repository.markDocumentFailed).toHaveBeenCalledWith(DOCUMENT_ID, providerError);
    expect(idempotency.release).toHaveBeenCalledWith(DOCUMENT_ID, 'owner-2');
  });

  it('processes pending outbox event and marks it published', async () => {
    repository.findPendingEvents.mockResolvedValue([makeOutboxEvent()]);
    repository.markEventPublishing.mockResolvedValue(true);
    idempotency.begin.mockResolvedValue({ state: 'duplicate' });

    await expect(service.processPendingOnce(1)).resolves.toBe(1);

    expect(idempotency.begin).toHaveBeenCalledWith(DOCUMENT_ID);
    expect(repository.markEventPublished).toHaveBeenCalledWith(EVENT_ID);
  });

  it('recovers a publishing event whose document was left processing', async () => {
    idempotency.begin.mockResolvedValue({ state: 'acquired', ownerToken: 'owner-recovery' });
    repository.findDocumentForIngest.mockResolvedValue(
      makeDocument({ ingestStatus: 'PROCESSING' }),
    );
    repository.markDocumentProcessing.mockResolvedValue(true);
    storageProvider.downloadObject.mockResolvedValue(Buffer.from('Nội dung phục hồi ingest'));
    embeddingProvider.embed.mockResolvedValue(makeEmbedding());

    await expect(service.processEvent(makeOutboxEvent({ status: 'PUBLISHING' }))).resolves.toBe(
      true,
    );

    expect(idempotency.begin).toHaveBeenCalledWith(DOCUMENT_ID);
    expect(repository.markDocumentProcessing).toHaveBeenCalledWith(DOCUMENT_ID);
    expect(repository.replaceChunksAndComplete).toHaveBeenCalled();
    expect(repository.markEventPublished).toHaveBeenCalledWith(EVENT_ID);
  });

  it('discards malformed outbox event payload without retrying', async () => {
    repository.markEventPublishing.mockResolvedValue(true);

    await expect(service.processEvent(makeOutboxEvent({ payload: { bad: true } }))).resolves.toBe(
      false,
    );

    expect(repository.markEventDiscarded).toHaveBeenCalledWith(EVENT_ID, expect.any(Error));
    expect(repository.markEventFailed).not.toHaveBeenCalled();
  });

  it('discards outbox event when document ingest reaches max retry', async () => {
    const ingestError = new Error('provider unavailable');
    repository.markEventPublishing.mockResolvedValue(true);
    jest.spyOn(service, 'processDocumentWithOutcome').mockRejectedValueOnce(ingestError);

    await expect(service.processEvent(makeOutboxEvent({ retryCount: 4 }))).resolves.toBe(false);

    expect(repository.markEventDiscarded).toHaveBeenCalledWith(EVENT_ID, ingestError);
    expect(repository.markEventFailed).not.toHaveBeenCalled();
  });

  it('marks outbox event failed when document ingest can still retry', async () => {
    const ingestError = new Error('provider unavailable');
    repository.markEventPublishing.mockResolvedValue(true);
    jest.spyOn(service, 'processDocumentWithOutcome').mockRejectedValueOnce(ingestError);

    await expect(service.processEvent(makeOutboxEvent({ retryCount: 3 }))).resolves.toBe(false);

    expect(repository.markEventFailed).toHaveBeenCalledWith(EVENT_ID, ingestError);
    expect(repository.markEventDiscarded).not.toHaveBeenCalled();
  });

  it('settles a recovered publishing event without acquiring the outbox row again', async () => {
    jest.spyOn(service, 'processDocumentWithOutcome').mockResolvedValueOnce('settled');

    await expect(service.processEvent(makeOutboxEvent({ status: 'PUBLISHING' }))).resolves.toBe(
      true,
    );

    expect(repository.markEventPublishing).not.toHaveBeenCalled();
    expect(repository.markEventPublished).toHaveBeenCalledWith(EVENT_ID);
  });

  it('leaves a publishing event untouched while its Redis processing lease is owned', async () => {
    jest.spyOn(service, 'processDocumentWithOutcome').mockResolvedValueOnce('locked');

    await expect(service.processEvent(makeOutboxEvent({ status: 'PUBLISHING' }))).resolves.toBe(
      false,
    );

    expect(repository.markEventPublishing).not.toHaveBeenCalled();
    expect(repository.markEventPublished).not.toHaveBeenCalled();
  });
});

function makeEnv(): Env {
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

export function makeEmbedding(): number[] {
  return Array.from({ length: EMBEDDING_DIMENSIONS }, (_, index) => index / EMBEDDING_DIMENSIONS);
}

export function makeDocument(overrides: Partial<KnowledgeDocument> = {}): KnowledgeDocument {
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

export function makeOutboxEvent(
  overrides: Partial<RagIngestOutboxEvent> = {},
): RagIngestOutboxEvent {
  return { ...makeOutboxEventBase(), ...overrides };
}

function makeOutboxEventBase(): RagIngestOutboxEvent {
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
  };
}
