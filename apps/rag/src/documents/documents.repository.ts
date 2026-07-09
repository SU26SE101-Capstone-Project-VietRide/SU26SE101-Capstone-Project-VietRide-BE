import { Injectable } from '@nestjs/common';
import type { KnowledgeDocument, Prisma } from '../generated/rag-prisma-client';
import { OutboxEventStatus } from '../generated/rag-prisma-client';
import { RagPrismaService } from '../prisma/rag-prisma.service';
import { RAG_DOCUMENT_INGEST_REQUESTED_EVENT } from './documents.constants';
import type {
  ApproveKnowledgeDocumentInput,
  CreateApprovedKnowledgeDocumentInput,
  CreateKnowledgeDocumentInput,
  ListKnowledgeDocumentsInput,
  ListKnowledgeDocumentsResult,
} from './documents.types';

@Injectable()
export class DocumentsRepository {
  constructor(private readonly prisma: RagPrismaService) {}

  async create(input: CreateKnowledgeDocumentInput): Promise<KnowledgeDocument> {
    return this.prisma.knowledgeDocument.create({
      data: this.toCreateData(input),
    });
  }

  async createApproved(input: CreateApprovedKnowledgeDocumentInput): Promise<KnowledgeDocument> {
    return this.prisma.$transaction(async (tx) => {
      const document = await tx.knowledgeDocument.create({
        data: {
          ...this.toCreateData(input),
          status: 'APPROVED',
          ingestStatus: 'PENDING',
          approvedByUserId: input.approvedByUserId,
          approvedAt: new Date(),
        },
      });

      await tx.outboxEvent.create({
        data: this.toIngestRequestedOutboxData(document),
      });

      return document;
    });
  }

  async findById(documentId: string): Promise<KnowledgeDocument | null> {
    return this.prisma.knowledgeDocument.findUnique({
      where: { id: documentId },
    });
  }

  async list(input: ListKnowledgeDocumentsInput): Promise<ListKnowledgeDocumentsResult> {
    const where = this.toListWhere(input);
    const orderBy = this.toListOrderBy(input);
    const skip = (input.page - 1) * input.pageSize;

    const [items, totalItems] = await this.prisma.$transaction([
      this.prisma.knowledgeDocument.findMany({
        where,
        orderBy,
        skip,
        take: input.pageSize,
      }),
      this.prisma.knowledgeDocument.count({ where }),
    ]);

    return { items, totalItems };
  }

  async approve(input: ApproveKnowledgeDocumentInput): Promise<KnowledgeDocument> {
    return this.prisma.$transaction(async (tx) => {
      const document = await tx.knowledgeDocument.update({
        where: { id: input.documentId },
        data: {
          status: 'APPROVED',
          ingestStatus: 'PENDING',
          ingestError: null,
          approvedByUserId: input.approvedByUserId,
          approvedAt: new Date(),
        },
      });

      await tx.outboxEvent.create({
        data: this.toIngestRequestedOutboxData(document),
      });

      return document;
    });
  }

  private toCreateData(input: CreateKnowledgeDocumentInput): Prisma.KnowledgeDocumentUncheckedCreateInput {
    return {
      title: input.title,
      storagePath: input.storagePath,
      fileName: input.fileName,
      mimeType: input.mimeType,
      fileSize: input.fileSize,
      fileType: input.fileType,
      accessLevel: input.accessLevel,
      category: input.category,
      documentType: input.documentType,
      audienceRoles: input.audienceRoles,
      language: input.language,
      uploadedByUserId: input.uploadedByUserId,
      ...(input.description ? { description: input.description } : {}),
      ...(input.operatorId ? { operatorId: input.operatorId } : {}),
    };
  }

  private toListWhere(input: ListKnowledgeDocumentsInput): Prisma.KnowledgeDocumentWhereInput {
    return {
      ...(input.status ? { status: input.status } : {}),
      ...(input.ingestStatus ? { ingestStatus: input.ingestStatus } : {}),
      ...(input.accessLevel ? { accessLevel: input.accessLevel } : {}),
      ...(input.category ? { category: input.category } : {}),
      ...(input.documentType ? { documentType: input.documentType } : {}),
      ...(input.operatorId ? { operatorId: input.operatorId } : {}),
      ...(input.q
        ? {
            OR: [
              { title: { contains: input.q, mode: 'insensitive' } },
              { fileName: { contains: input.q, mode: 'insensitive' } },
              { description: { contains: input.q, mode: 'insensitive' } },
            ],
          }
        : {}),
    };
  }

  private toListOrderBy(input: ListKnowledgeDocumentsInput): Prisma.KnowledgeDocumentOrderByWithRelationInput {
    return { [input.sortBy]: input.sortDir };
  }

  private toIngestRequestedOutboxData(document: KnowledgeDocument): Prisma.OutboxEventUncheckedCreateInput {
    return {
      eventType: RAG_DOCUMENT_INGEST_REQUESTED_EVENT,
      status: OutboxEventStatus.PENDING,
      payload: {
        documentId: document.id,
        storagePath: document.storagePath,
        fileType: document.fileType,
        accessLevel: document.accessLevel,
        operatorId: document.operatorId,
      },
    };
  }
}
