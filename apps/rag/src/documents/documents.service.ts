import {
  BadRequestException,
  ConflictException,
  ForbiddenException,
  Inject,
  Injectable,
  NotFoundException,
} from '@nestjs/common';
import { extname } from 'node:path';
import pino from 'pino';
import { STORAGE_PROVIDER } from '../app/tokens';
import type { RagInternalUser } from '../auth/rag-internal-user.types';
import { RAG_RUNTIME_CONFIG_KEYS } from '../config/runtime-config.registry';
import { RuntimeConfigService, type RuntimeConfigSnapshot } from '../config/runtime-config.service';
import type {
  KnowledgeDocumentAccess,
  KnowledgeDocumentCategory,
  KnowledgeDocumentFileType,
  KnowledgeDocumentType,
} from '../generated/rag-prisma-client';
import type { StorageProvider } from '../providers/storage.provider';
import type { CreateDocumentDto } from './dto/create-document.dto';
import {
  RAG_DOCUMENT_PREVIEW_URL_TTL_SECONDS,
  RAG_DOCUMENT_STORAGE_PREFIX,
} from './documents.constants';
import { DocumentsRepository } from './documents.repository';
import {
  KnowledgeDocumentPage,
  KnowledgeDocumentResponse,
  ListKnowledgeDocumentsInput,
  toKnowledgeDocumentResponse,
  UploadedDocumentFile,
} from './documents.types';

const SYSTEM_ADMIN_ROLE = 'SYSTEM_ADMIN';
const logger = pino({ name: 'RagDocumentsService' });

@Injectable()
export class DocumentsService {
  constructor(
    private readonly documentsRepository: DocumentsRepository,
    @Inject(STORAGE_PROVIDER) private readonly storageProvider: StorageProvider,
    private readonly runtimeConfig: RuntimeConfigService,
  ) {}

  async create(
    dto: CreateDocumentDto,
    file: UploadedDocumentFile | undefined,
    user: RagInternalUser | undefined,
    operationId?: string,
  ): Promise<KnowledgeDocumentResponse> {
    this.assertSystemAdmin(user);
    const runtimeConfig = await this.runtimeConfig.getSnapshot();
    const validFile = this.validateFile(file, runtimeConfig);
    this.validateTaxonomy(dto.accessLevel, dto.category, dto.operatorId);

    const storagePath = this.buildStoragePath(validFile.originalname, operationId);
    await this.storageProvider.uploadObject({
      storagePath,
      contentType: validFile.mimetype,
      body: validFile.buffer,
    });

    const document = await this.documentsRepository.createApproved({
      title: dto.title,
      storagePath,
      fileName: this.sanitizeFileName(validFile.originalname),
      mimeType: validFile.mimetype,
      fileSize: BigInt(validFile.size),
      fileType: this.detectFileType(validFile, runtimeConfig),
      accessLevel: dto.accessLevel as KnowledgeDocumentAccess,
      category: dto.category as KnowledgeDocumentCategory,
      documentType: dto.documentType as KnowledgeDocumentType,
      audienceRoles: dto.audienceRoles,
      language: dto.language,
      uploadedByUserId: user.sub,
      approvedByUserId: user.sub,
      ...(dto.description ? { description: dto.description } : {}),
      ...(dto.operatorId ? { operatorId: dto.operatorId } : {}),
    });
    const previewUrl = await this.storageProvider.createSignedReadUrl({
      storagePath: document.storagePath,
      expiresInSeconds: RAG_DOCUMENT_PREVIEW_URL_TTL_SECONDS,
    });

    logger.info({ documentId: document.id }, 'RAG document uploaded and ingest requested');
    return toKnowledgeDocumentResponse(document, previewUrl);
  }

  async approve(
    documentId: string,
    user: RagInternalUser | undefined,
  ): Promise<KnowledgeDocumentResponse> {
    this.assertSystemAdmin(user);
    const document = await this.documentsRepository.findById(documentId);
    if (!document) {
      throw new NotFoundException({
        errorCode: 'RAG_DOCUMENT_NOT_FOUND',
        detail: `Document ${documentId} not found`,
      });
    }
    if (document.status !== 'PENDING_REVIEW') {
      throw new ConflictException({
        errorCode: 'RAG_DOCUMENT_STATUS_CONFLICT',
        detail: `Document ${documentId} is not pending review`,
      });
    }

    const approved = await this.documentsRepository.approve({
      documentId,
      approvedByUserId: user.sub,
    });
    logger.info({ documentId }, 'RAG document approved');
    return toKnowledgeDocumentResponse(approved);
  }

  async list(
    query: ListKnowledgeDocumentsInput,
    user: RagInternalUser | undefined,
  ): Promise<KnowledgeDocumentPage> {
    this.assertSystemAdmin(user);
    const { items, totalItems } = await this.documentsRepository.list(query);
    const totalPages = Math.ceil(totalItems / query.pageSize);

    return {
      items: items.map((document) => toKnowledgeDocumentResponse(document)),
      page: query.page,
      pageSize: query.pageSize,
      totalItems,
      totalPages,
      hasNextPage: query.page < totalPages,
      hasPreviousPage: query.page > 1,
    };
  }

  private assertSystemAdmin(user: RagInternalUser | undefined): asserts user is RagInternalUser {
    if (user?.role !== SYSTEM_ADMIN_ROLE) {
      throw new ForbiddenException({
        errorCode: 'INSUFFICIENT_ROLE',
        detail: 'SYSTEM_ADMIN role is required',
      });
    }
  }

  private validateFile(
    file: UploadedDocumentFile | undefined,
    runtimeConfig: RuntimeConfigSnapshot,
  ): UploadedDocumentFile {
    if (!file) {
      throw new BadRequestException({
        errorCode: 'RAG_DOCUMENT_FILE_REQUIRED',
        detail: 'Document file is required',
      });
    }
    if (file.size <= 0 || file.size > runtimeConfig.getNumber(RAG_RUNTIME_CONFIG_KEYS.documentMaxFileBytes)) {
      throw new BadRequestException({
        errorCode: 'RAG_DOCUMENT_FILE_INVALID_SIZE',
        detail: 'Document file size is invalid',
      });
    }
    if (!runtimeConfig.getStringList(RAG_RUNTIME_CONFIG_KEYS.documentAllowedMimeTypes).includes(file.mimetype)) {
      throw new BadRequestException({
        errorCode: 'RAG_DOCUMENT_FILE_INVALID_TYPE',
        detail: 'Only TXT and MARKDOWN files are supported',
      });
    }
    this.detectFileType(file, runtimeConfig);
    return file;
  }

  private validateTaxonomy(
    accessLevel: CreateDocumentDto['accessLevel'],
    category: CreateDocumentDto['category'],
    operatorId: string | undefined,
  ): void {
    if (accessLevel === 'PUBLIC' && (category !== 'CUSTOMER_SUPPORT' || operatorId)) {
      throw new BadRequestException({
        errorCode: 'RAG_DOCUMENT_TAXONOMY_INVALID',
        detail: 'PUBLIC documents must be global customer support documents',
      });
    }
    if (accessLevel === 'OPERATOR' && category !== 'OPERATOR_POLICY') {
      throw new BadRequestException({
        errorCode: 'RAG_DOCUMENT_TAXONOMY_INVALID',
        detail: 'OPERATOR documents must use OPERATOR_POLICY category',
      });
    }
    if (accessLevel === 'ADMIN' && category !== 'PLATFORM_ADMIN') {
      throw new BadRequestException({
        errorCode: 'RAG_DOCUMENT_TAXONOMY_INVALID',
        detail: 'ADMIN documents must use PLATFORM_ADMIN category',
      });
    }
  }

  private detectFileType(
    file: UploadedDocumentFile,
    runtimeConfig: RuntimeConfigSnapshot,
  ): KnowledgeDocumentFileType {
    const extension = extname(file.originalname).toLowerCase();
    const allowedExtensions = runtimeConfig.getStringList(RAG_RUNTIME_CONFIG_KEYS.documentAllowedExtensions);
    if (!allowedExtensions.includes(extension)) {
      throw new BadRequestException({
        errorCode: 'RAG_DOCUMENT_FILE_INVALID_TYPE',
        detail: 'Only configured knowledge document file types are supported',
      });
    }
    if (extension === '.txt') return 'TXT';
    if (extension === '.md' || extension === '.markdown') return 'MARKDOWN';
    throw new BadRequestException({
      errorCode: 'RAG_DOCUMENT_FILE_INVALID_TYPE',
      detail: 'Only TXT and MARKDOWN files are supported',
    });
  }

  private buildStoragePath(originalFileName: string, operationId?: string): string {
    const extension = extname(originalFileName).toLowerCase();
    if (!operationId) {
      throw new BadRequestException({
        errorCode: 'IDEMPOTENCY_KEY_REQUIRED',
        detail: 'Idempotency-Key is required for document upload',
      });
    }
    return `${RAG_DOCUMENT_STORAGE_PREFIX}/${operationId}${extension}`;
  }

  private sanitizeFileName(originalFileName: string): string {
    const sanitized = originalFileName.replace(/[/\\]/g, '_').trim();
    return sanitized.length > 0 ? sanitized : 'document.txt';
  }
}
