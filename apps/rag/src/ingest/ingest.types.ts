import type {
  KnowledgeDocument,
  KnowledgeDocumentFileType,
  OutboxEvent,
} from '../generated/rag-prisma-client';

export type RagIngestOutboxEvent = OutboxEvent;
export type RagIngestDocument = KnowledgeDocument;

export interface RagDocumentIngestPayload {
  documentId: string;
  storagePath: string;
  fileType: KnowledgeDocumentFileType;
  accessLevel: string;
  operatorId: string | null;
}

export interface RagIngestChunk {
  chunkIndex: number;
  sectionHeader: string | null;
  content: string;
  tokenCount: number;
  embedding: number[];
}

export interface RagTextChunk {
  chunkIndex: number;
  sectionHeader: string | null;
  content: string;
  tokenCount: number;
}
