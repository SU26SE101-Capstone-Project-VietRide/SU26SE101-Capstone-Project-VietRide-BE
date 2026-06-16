import { Injectable } from '@nestjs/common';
import { RagPrismaService } from '../prisma/rag-prisma.service';
import { RAG_DOCUMENT_INGEST_REQUESTED_EVENT } from '../documents/documents.constants';
import { RAG_INGEST_MAX_ERROR_LENGTH, RAG_INGEST_MAX_RETRY } from './ingest.constants';
import type { RagIngestChunk, RagIngestDocument, RagIngestOutboxEvent } from './ingest.types';

@Injectable()
export class IngestRepository {
  constructor(private readonly prisma: RagPrismaService) {}

  async findPendingEvents(limit: number): Promise<RagIngestOutboxEvent[]> {
    return this.prisma.outboxEvent.findMany({
      where: {
        eventType: RAG_DOCUMENT_INGEST_REQUESTED_EVENT,
        status: {
          in: ['PENDING', 'FAILED'],
        },
        retryCount: {
          lt: RAG_INGEST_MAX_RETRY,
        },
      },
      orderBy: {
        createdAt: 'asc',
      },
      take: limit,
    });
  }

  async markEventPublishing(eventId: string): Promise<boolean> {
    const result = await this.prisma.outboxEvent.updateMany({
      where: {
        id: eventId,
        eventType: RAG_DOCUMENT_INGEST_REQUESTED_EVENT,
        status: {
          in: ['PENDING', 'FAILED'],
        },
        retryCount: {
          lt: RAG_INGEST_MAX_RETRY,
        },
      },
      data: {
        status: 'PUBLISHING',
        lastError: null,
      },
    });

    return result.count === 1;
  }

  async markEventPublished(eventId: string): Promise<void> {
    await this.prisma.outboxEvent.update({
      where: { id: eventId },
      data: {
        status: 'PUBLISHED',
        publishedAt: new Date(),
        lastError: null,
      },
    });
  }

  async markEventFailed(eventId: string, error: unknown): Promise<void> {
    await this.prisma.outboxEvent.update({
      where: { id: eventId },
      data: {
        status: 'FAILED',
        retryCount: {
          increment: 1,
        },
        lastError: this.formatError(error),
      },
    });
  }

  async markEventDiscarded(eventId: string, error: unknown): Promise<void> {
    await this.prisma.outboxEvent.update({
      where: { id: eventId },
      data: {
        status: 'DISCARDED',
        retryCount: {
          increment: 1,
        },
        lastError: this.formatError(error),
      },
    });
  }

  async findDocumentForIngest(documentId: string): Promise<RagIngestDocument | null> {
    return this.prisma.knowledgeDocument.findUnique({
      where: { id: documentId },
    });
  }

  async markDocumentProcessing(documentId: string): Promise<boolean> {
    const result = await this.prisma.knowledgeDocument.updateMany({
      where: {
        id: documentId,
        status: 'APPROVED',
        ingestStatus: {
          in: ['PENDING', 'FAILED'],
        },
      },
      data: {
        ingestStatus: 'PROCESSING',
        ingestError: null,
      },
    });

    return result.count === 1;
  }

  async replaceChunksAndComplete(
    document: RagIngestDocument,
    chunks: RagIngestChunk[],
    embeddingModel: string,
    embeddingDimensions: number,
  ): Promise<void> {
    await this.prisma.$transaction(async (tx) => {
      await tx.knowledgeChunk.deleteMany({
        where: { documentId: document.id },
      });

      for (const chunk of chunks) {
        await tx.$executeRaw`
          INSERT INTO vietride_rag.knowledge_chunks (
            document_id,
            operator_id,
            document_title,
            section_header,
            document_type,
            chunk_index,
            content,
            token_count,
            embedding,
            search_vector
          )
          VALUES (
            ${document.id}::uuid,
            ${document.operatorId}::uuid,
            ${document.title},
            ${chunk.sectionHeader},
            ${document.documentType}::vietride_rag.knowledge_document_type,
            ${chunk.chunkIndex},
            ${chunk.content},
            ${chunk.tokenCount},
            ${this.toVectorLiteral(chunk.embedding)}::halfvec,
            to_tsvector('simple', ${chunk.content})
          )
        `;
      }

      await tx.knowledgeDocument.update({
        where: { id: document.id },
        data: {
          ingestStatus: 'COMPLETED',
          ingestError: null,
          ingestedAt: new Date(),
          chunkCount: chunks.length,
          embeddingModel,
          embeddingDimensions,
        },
      });
    });
  }

  async markDocumentFailed(documentId: string, error: unknown): Promise<void> {
    await this.prisma.knowledgeDocument.update({
      where: { id: documentId },
      data: {
        ingestStatus: 'FAILED',
        ingestError: this.formatError(error),
      },
    });
  }

  async countChunks(documentId: string): Promise<number> {
    return this.prisma.knowledgeChunk.count({
      where: { documentId },
    });
  }

  private toVectorLiteral(embedding: number[]): string {
    return `[${embedding.map((value) => value.toString()).join(',')}]`;
  }

  private formatError(error: unknown): string {
    const message = error instanceof Error ? error.message : String(error);
    return message.slice(0, RAG_INGEST_MAX_ERROR_LENGTH);
  }
}
