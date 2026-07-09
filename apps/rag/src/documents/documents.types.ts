import type {
  KnowledgeDocument,
  KnowledgeDocumentAccess,
  KnowledgeDocumentCategory,
  KnowledgeDocumentFileType,
  KnowledgeDocumentIngestStatus,
  KnowledgeDocumentStatus,
  KnowledgeDocumentType,
} from '../generated/rag-prisma-client';
import type { ListDocumentsQueryDto } from './dto/list-documents.dto';

export interface UploadedDocumentFile {
  originalname: string;
  mimetype: string;
  size: number;
  buffer: Buffer;
}

export interface CreateKnowledgeDocumentInput {
  title: string;
  description?: string;
  storagePath: string;
  fileName: string;
  mimeType: string;
  fileSize: bigint;
  fileType: KnowledgeDocumentFileType;
  accessLevel: KnowledgeDocumentAccess;
  operatorId?: string;
  category: KnowledgeDocumentCategory;
  documentType: KnowledgeDocumentType;
  audienceRoles: string[];
  language: string;
  uploadedByUserId: string;
}

export interface CreateApprovedKnowledgeDocumentInput extends CreateKnowledgeDocumentInput {
  approvedByUserId: string;
}

export interface ApproveKnowledgeDocumentInput {
  documentId: string;
  approvedByUserId: string;
}

export type ListKnowledgeDocumentsInput = ListDocumentsQueryDto;

export interface ListKnowledgeDocumentsResult {
  items: KnowledgeDocument[];
  totalItems: number;
}

export interface KnowledgeDocumentResponse {
  id: string;
  title: string;
  description: string | null;
  storagePath: string;
  fileName: string;
  mimeType: string;
  fileSize: string;
  fileType: KnowledgeDocumentFileType;
  accessLevel: KnowledgeDocumentAccess;
  operatorId: string | null;
  category: KnowledgeDocumentCategory;
  documentType: KnowledgeDocumentType;
  audienceRoles: string[];
  language: string;
  status: string;
  ingestStatus: string;
  previewUrl?: string;
  createdAt: string;
  updatedAt: string;
  approvedAt: string | null;
}

export interface KnowledgeDocumentPage {
  items: KnowledgeDocumentResponse[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

export function toKnowledgeDocumentResponse(
  document: KnowledgeDocument,
  previewUrl?: string,
): KnowledgeDocumentResponse {
  return {
    id: document.id,
    title: document.title,
    description: document.description,
    storagePath: document.storagePath,
    fileName: document.fileName,
    mimeType: document.mimeType,
    fileSize: document.fileSize.toString(),
    fileType: document.fileType,
    accessLevel: document.accessLevel,
    operatorId: document.operatorId,
    category: document.category,
    documentType: document.documentType,
    audienceRoles: document.audienceRoles,
    language: document.language,
    status: document.status,
    ingestStatus: document.ingestStatus,
    ...(previewUrl ? { previewUrl } : {}),
    createdAt: document.createdAt.toISOString(),
    updatedAt: document.updatedAt.toISOString(),
    approvedAt: document.approvedAt?.toISOString() ?? null,
  };
}

export type KnowledgeDocumentListSortBy =
  | 'createdAt'
  | 'updatedAt'
  | 'title'
  | 'status'
  | 'ingestStatus';

export interface KnowledgeDocumentListFilters {
  status?: KnowledgeDocumentStatus;
  ingestStatus?: KnowledgeDocumentIngestStatus;
  accessLevel?: KnowledgeDocumentAccess;
  category?: KnowledgeDocumentCategory;
  documentType?: KnowledgeDocumentType;
  operatorId?: string;
  q?: string;
}
