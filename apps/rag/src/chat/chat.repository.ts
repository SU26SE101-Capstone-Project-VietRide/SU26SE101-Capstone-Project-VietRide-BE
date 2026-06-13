import { Injectable } from '@nestjs/common';
import type {
  KnowledgeDocumentAccess,
  RagConversation,
  RagConversationRole,
  RagMessage,
} from '../generated/rag-prisma-client';
import { RagPrismaService } from '../prisma/rag-prisma.service';
import type { RagRetrievedChunk } from './chat.types';

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

  async searchChunks(input: {
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
