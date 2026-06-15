import {
  BadRequestException,
  Inject,
  Injectable,
  ServiceUnavailableException,
} from '@nestjs/common';
import pino from 'pino';
import { z } from 'zod';
import { EMBEDDING_PROVIDER, ENV_TOKEN, STORAGE_PROVIDER } from '../app/tokens';
import type { Env } from '../config/env.schema';
import type { EmbeddingProvider } from '../providers/embedding.provider';
import type { StorageProvider } from '../providers/storage.provider';
import { RAG_INGEST_EXPECTED_EMBEDDING_DIMENSIONS, RAG_INGEST_MAX_RETRY } from './ingest.constants';
import { IngestIdempotencyService } from './ingest-idempotency.service';
import { IngestRepository } from './ingest.repository';
import { chunkRagText } from './ingest-text-chunker.service';
import type {
  RagDocumentIngestPayload,
  RagIngestChunk,
  RagIngestDocument,
  RagIngestOutboxEvent,
  RagTextChunk,
} from './ingest.types';

const ingestPayloadSchema = z.object({
  documentId: z.string().uuid(),
  storagePath: z.string().min(1),
  fileType: z.enum(['TXT', 'MARKDOWN']),
  accessLevel: z.enum(['PUBLIC', 'OPERATOR', 'ADMIN']),
  operatorId: z.string().uuid().nullable(),
});

@Injectable()
export class IngestService {
  private readonly logger = pino({ name: IngestService.name });

  constructor(
    private readonly repository: IngestRepository,
    private readonly idempotency: IngestIdempotencyService,
    @Inject(STORAGE_PROVIDER) private readonly storageProvider: StorageProvider,
    @Inject(EMBEDDING_PROVIDER) private readonly embeddingProvider: EmbeddingProvider,
    @Inject(ENV_TOKEN) private readonly env: Env,
  ) {}

  async processPendingOnce(limit: number): Promise<number> {
    const events = await this.repository.findPendingEvents(limit);
    let processed = 0;

    for (const event of events) {
      const didProcess = await this.processEvent(event);
      if (didProcess) processed += 1;
    }

    return processed;
  }

  async processEvent(event: RagIngestOutboxEvent): Promise<boolean> {
    const locked = await this.repository.markEventPublishing(event.id);
    if (!locked) return false;

    let payload: RagDocumentIngestPayload;
    try {
      payload = ingestPayloadSchema.parse(event.payload);
    } catch (error) {
      await this.repository.markEventDiscarded(event.id, error);
      this.logger.warn({ eventId: event.id, err: error }, 'Dropping malformed RAG ingest event');
      return false;
    }

    try {
      await this.processDocument(payload.documentId);
      await this.repository.markEventPublished(event.id);
      return true;
    } catch (error) {
      if (event.retryCount + 1 >= RAG_INGEST_MAX_RETRY) {
        await this.repository.markEventDiscarded(event.id, error);
      } else {
        await this.repository.markEventFailed(event.id, error);
      }
      this.logger.warn(
        { eventId: event.id, documentId: payload.documentId, err: error },
        'RAG document ingest failed',
      );
      return false;
    }
  }

  async processDocument(documentId: string): Promise<boolean> {
    const state = await this.idempotency.begin(documentId);
    if (state !== 'acquired') {
      this.logger.info({ documentId, state }, 'Skipping RAG document ingest');
      return false;
    }

    try {
      const document = await this.loadProcessableDocument(documentId);
      const processing = await this.repository.markDocumentProcessing(documentId);
      if (!processing) {
        await this.idempotency.markProcessed(documentId);
        return false;
      }

      try {
        const text = await this.downloadText(document);
        const textChunks = chunkRagText(text);
        const chunks = await this.embedChunks(textChunks);
        const embeddingDimensions = chunks[0]?.embedding.length;
        if (!embeddingDimensions) {
          throw new ServiceUnavailableException({
            errorCode: 'RAG_EMBEDDING_INVALID',
            detail: 'Embedding provider returned no vectors',
          });
        }
        await this.repository.replaceChunksAndComplete(
          document,
          chunks,
          this.env.OPENROUTER_EMBEDDING_MODEL,
          embeddingDimensions,
        );
        await this.idempotency.markProcessed(documentId);
        this.logger.info({ documentId, chunkCount: chunks.length }, 'RAG document ingest completed');
        return true;
      } catch (error) {
        await this.repository.markDocumentFailed(documentId, error);
        await this.idempotency.release(documentId);
        throw error;
      }
    } catch (error) {
      await this.idempotency.release(documentId);
      throw error;
    }
  }

  private async loadProcessableDocument(documentId: string): Promise<RagIngestDocument> {
    const document = await this.repository.findDocumentForIngest(documentId);
    if (!document) {
      throw new BadRequestException({
        errorCode: 'RAG_DOCUMENT_NOT_FOUND',
        detail: `Document ${documentId} not found`,
      });
    }
    if (document.status !== 'APPROVED') {
      throw new BadRequestException({
        errorCode: 'RAG_DOCUMENT_NOT_APPROVED',
        detail: `Document ${documentId} is not approved`,
      });
    }
    if (document.fileType !== 'TXT' && document.fileType !== 'MARKDOWN') {
      throw new BadRequestException({
        errorCode: 'RAG_DOCUMENT_FILE_UNSUPPORTED',
        detail: 'Only TXT and MARKDOWN files are supported for ingest',
      });
    }

    return document;
  }

  private async downloadText(document: RagIngestDocument): Promise<string> {
    const object = await this.storageProvider.downloadObject(document.storagePath);
    return object.toString('utf8');
  }

  private async embedChunks(chunks: RagTextChunk[]): Promise<RagIngestChunk[]> {
    // MVP trade-off: sequential embedding keeps ingest simple for small admin-approved documents.
    const embedded: RagIngestChunk[] = [];

    for (const chunk of chunks) {
      const embedding = await this.embeddingProvider.embed({ input: chunk.content });
      this.assertEmbeddingDimension(embedding);
      embedded.push({ ...chunk, embedding });
    }

    return embedded;
  }

  private assertEmbeddingDimension(embedding: number[]): void {
    const expectedDimension =
      this.env.RAG_EMBEDDING_DIMENSIONS === 'auto'
        ? RAG_INGEST_EXPECTED_EMBEDDING_DIMENSIONS
        : this.env.RAG_EMBEDDING_DIMENSIONS;

    if (
      embedding.length !== expectedDimension ||
      embedding.some((value) => !Number.isFinite(value))
    ) {
      throw new ServiceUnavailableException({
        errorCode: 'RAG_EMBEDDING_DIMENSION_MISMATCH',
        detail: 'Embedding vector dimension does not match RAG database dimension',
      });
    }
  }
}
