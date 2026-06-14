import { Injectable } from '@nestjs/common';
import type {
  KnowledgeDocumentAccess,
  RagConversation,
  RagConversationRole,
  RagMessage,
} from '../generated/rag-prisma-client';
import { RagPrismaService } from '../prisma/rag-prisma.service';
import type { RagFeedbackListItem, RagFeedbackPage, RagMessageWithConversation, RagRetrievedChunk } from './chat.types';

const HYBRID_SEARCH_CANDIDATE_LIMIT = 10;
const RRF_RANK_OFFSET = 60;

@Injectable()
export class ChatRepository {
  constructor(private readonly prisma: RagPrismaService) {}

  async findConversation(conversationId: string): Promise<RagConversation | null> {
    return this.prisma.ragConversation.findUnique({
      where: { id: conversationId },
    });
  }

  async createConversation(input: {
    userId: string;
    role: RagConversationRole;
    operatorId?: string;
  }): Promise<RagConversation> {
    return this.prisma.ragConversation.create({
      data: {
        userId: input.userId,
        role: input.role,
        ...(input.operatorId ? { operatorId: input.operatorId } : {}),
      },
    });
  }

  async createUserMessage(conversationId: string, content: string): Promise<RagMessage> {
    return this.createMessage(conversationId, 'USER', content, []);
  }

  async createAssistantMessage(
    conversationId: string,
    content: string,
    citedChunkIds: string[],
  ): Promise<RagMessage> {
    return this.createMessage(conversationId, 'ASSISTANT', content, citedChunkIds);
  }

  async findRecentMessages(conversationId: string, limit: number): Promise<RagMessage[]> {
    const messages = await this.prisma.ragMessage.findMany({
      where: { conversationId },
      orderBy: { createdAt: 'desc' },
      take: limit,
    });
    return messages.reverse();
  }

  async countMessages(conversationId: string): Promise<number> {
    return this.prisma.ragMessage.count({
      where: { conversationId },
    });
  }

  async updateConversationSummary(input: {
    conversationId: string;
    summary: string;
    summaryFromMessageId: string;
  }): Promise<RagConversation> {
    return this.prisma.ragConversation.update({
      where: { id: input.conversationId },
      data: {
        summary: input.summary,
        summaryUpdatedAt: new Date(),
        summaryFromMessageId: input.summaryFromMessageId,
      },
    });
  }

  async findMessageWithConversation(messageId: string): Promise<RagMessageWithConversation | null> {
    return this.prisma.ragMessage.findUnique({
      where: { id: messageId },
      include: { conversation: true },
    });
  }

  async upsertMessageFeedback(input: {
    message: RagMessageWithConversation;
    userId: string;
    rating: number;
  }) {
    return this.prisma.messageFeedback.upsert({
      where: { messageId: input.message.id },
      create: {
        messageId: input.message.id,
        conversationId: input.message.conversationId,
        userId: input.userId,
        rating: input.rating,
        chunkIds: input.message.citedChunkIds,
        responseLength: input.message.content.length,
      },
      update: {
        rating: input.rating,
        chunkIds: input.message.citedChunkIds,
        responseLength: input.message.content.length,
        updatedAt: new Date(),
      },
    });
  }

  async listFeedback(input: {
    page: number;
    pageSize: number;
    sortBy: 'createdAt' | 'rating';
    sortDir: 'asc' | 'desc';
  }): Promise<RagFeedbackPage> {
    const skip = (input.page - 1) * input.pageSize;
    const [items, totalItems] = await this.prisma.$transaction([
      this.prisma.messageFeedback.findMany({
        orderBy: { [input.sortBy]: input.sortDir },
        skip,
        take: input.pageSize,
        include: {
          message: {
            select: {
              id: true,
              role: true,
              content: true,
              citedChunkIds: true,
              createdAt: true,
            },
          },
          conversation: {
            select: {
              id: true,
              userId: true,
              operatorId: true,
              role: true,
            },
          },
        },
      }),
      this.prisma.messageFeedback.count(),
    ]);
    const totalPages = Math.ceil(totalItems / input.pageSize);
    return {
      items: items as RagFeedbackListItem[],
      page: input.page,
      pageSize: input.pageSize,
      totalItems,
      totalPages,
      hasNextPage: input.page < totalPages,
      hasPreviousPage: input.page > 1,
    };
  }

  async searchChunks(input: {
    queryText: string;
    queryEmbedding: number[];
    accessLevels: KnowledgeDocumentAccess[];
    operatorId?: string;
    limit: number;
    hybridSearchEnabled: boolean;
  }): Promise<RagRetrievedChunk[]> {
    if (input.hybridSearchEnabled) {
      return this.searchChunksHybrid(input);
    }
    return this.searchChunksVector(input);
  }

  private async searchChunksVector(input: {
    queryEmbedding: number[];
    accessLevels: KnowledgeDocumentAccess[];
    operatorId?: string;
    limit: number;
  }): Promise<RagRetrievedChunk[]> {
    return this.prisma.$queryRaw<RagRetrievedChunk[]>`
      SELECT
        c.id::text AS "id",
        c.document_id::text AS "documentId",
        c.document_title AS "documentTitle",
        c.section_header AS "sectionHeader",
        c.document_type AS "documentType",
        c.content,
        c.token_count AS "tokenCount",
        d.access_level AS "accessLevel",
        c.operator_id::text AS "operatorId",
        (c.embedding <=> ${this.toVectorLiteral(input.queryEmbedding)}::halfvec) AS "distance"
      FROM vietride_rag.knowledge_chunks c
      INNER JOIN vietride_rag.knowledge_documents d ON d.id = c.document_id
      WHERE d.status = 'APPROVED'::vietride_rag.knowledge_document_status
        AND d.ingest_status = 'COMPLETED'::vietride_rag.knowledge_document_ingest_status
        AND d.access_level = ANY(${input.accessLevels}::vietride_rag.knowledge_document_access[])
        AND (
          c.operator_id IS NULL
          OR (${input.operatorId ?? null}::uuid IS NOT NULL AND c.operator_id = ${input.operatorId ?? null}::uuid)
        )
      ORDER BY c.embedding <=> ${this.toVectorLiteral(input.queryEmbedding)}::halfvec
      LIMIT ${input.limit}
    `;
  }

  private async searchChunksHybrid(input: {
    queryText: string;
    queryEmbedding: number[];
    accessLevels: KnowledgeDocumentAccess[];
    operatorId?: string;
    limit: number;
  }): Promise<RagRetrievedChunk[]> {
    return this.prisma.$queryRaw<RagRetrievedChunk[]>`
      WITH scoped_chunks AS (
        SELECT
          c.id,
          c.document_id,
          c.document_title,
          c.section_header,
          c.document_type,
          c.content,
          c.token_count,
          c.operator_id,
          c.embedding,
          c.search_vector,
          d.access_level
        FROM vietride_rag.knowledge_chunks c
        INNER JOIN vietride_rag.knowledge_documents d ON d.id = c.document_id
        WHERE d.status = 'APPROVED'::vietride_rag.knowledge_document_status
          AND d.ingest_status = 'COMPLETED'::vietride_rag.knowledge_document_ingest_status
          AND d.access_level = ANY(${input.accessLevels}::vietride_rag.knowledge_document_access[])
          AND (
            c.operator_id IS NULL
            OR (${input.operatorId ?? null}::uuid IS NOT NULL AND c.operator_id = ${input.operatorId ?? null}::uuid)
          )
      ),
      fts_candidates AS (
        SELECT
          scoped_chunks.id,
          row_number() OVER (
            ORDER BY ts_rank_cd(scoped_chunks.search_vector, plainto_tsquery('simple', ${input.queryText})) DESC
          ) AS rank_fts
        FROM scoped_chunks
        WHERE scoped_chunks.search_vector @@ plainto_tsquery('simple', ${input.queryText})
        ORDER BY ts_rank_cd(scoped_chunks.search_vector, plainto_tsquery('simple', ${input.queryText})) DESC
        LIMIT ${HYBRID_SEARCH_CANDIDATE_LIMIT}
      ),
      vector_candidates AS (
        SELECT
          scoped_chunks.id,
          row_number() OVER (
            ORDER BY scoped_chunks.embedding <=> ${this.toVectorLiteral(input.queryEmbedding)}::halfvec
          ) AS rank_vector
        FROM scoped_chunks
        ORDER BY scoped_chunks.embedding <=> ${this.toVectorLiteral(input.queryEmbedding)}::halfvec
        LIMIT ${HYBRID_SEARCH_CANDIDATE_LIMIT}
      ),
      fused AS (
        SELECT
          COALESCE(fts_candidates.id, vector_candidates.id) AS id,
          COALESCE(1.0 / (${RRF_RANK_OFFSET} + fts_candidates.rank_fts), 0.0)
            + COALESCE(1.0 / (${RRF_RANK_OFFSET} + vector_candidates.rank_vector), 0.0) AS rrf_score
        FROM fts_candidates
        FULL OUTER JOIN vector_candidates ON vector_candidates.id = fts_candidates.id
      )
      SELECT
        scoped_chunks.id::text AS "id",
        scoped_chunks.document_id::text AS "documentId",
        scoped_chunks.document_title AS "documentTitle",
        scoped_chunks.section_header AS "sectionHeader",
        scoped_chunks.document_type AS "documentType",
        scoped_chunks.content,
        scoped_chunks.token_count AS "tokenCount",
        scoped_chunks.access_level AS "accessLevel",
        scoped_chunks.operator_id::text AS "operatorId",
        (scoped_chunks.embedding <=> ${this.toVectorLiteral(input.queryEmbedding)}::halfvec) AS "distance"
      FROM fused
      INNER JOIN scoped_chunks ON scoped_chunks.id = fused.id
      ORDER BY fused.rrf_score DESC, scoped_chunks.embedding <=> ${this.toVectorLiteral(input.queryEmbedding)}::halfvec
      LIMIT ${input.limit}
    `;
  }

  private async createMessage(
    conversationId: string,
    role: 'USER' | 'ASSISTANT',
    content: string,
    citedChunkIds: string[],
  ): Promise<RagMessage> {
    return this.prisma.$transaction(async (tx) => {
      const message = await tx.ragMessage.create({
        data: {
          conversationId,
          role,
          content,
          citedChunkIds,
        },
      });
      await tx.ragConversation.update({
        where: { id: conversationId },
        data: { lastMessageAt: new Date() },
      });
      return message;
    });
  }

  private toVectorLiteral(embedding: number[]): string {
    return `[${embedding.map((value) => value.toString()).join(',')}]`;
  }
}
