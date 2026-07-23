import { BadRequestException, ConflictException, ForbiddenException, NotFoundException } from '@nestjs/common';
import { RAG_RUNTIME_CONFIG_DEFINITIONS } from '../config/runtime-config.registry';
import { RuntimeConfigService, RuntimeConfigSnapshot } from '../config/runtime-config.service';
import type { KnowledgeDocument } from '../generated/rag-prisma-client';
import type { StorageProvider } from '../providers/storage.provider';
import { DocumentsRepository } from './documents.repository';
import { DocumentsService } from './documents.service';
import type { UploadedDocumentFile } from './documents.types';

const ADMIN_USER = {
  sub: '11111111-1111-1111-1111-111111111111',
  role: 'SYSTEM_ADMIN',
};
const PASSENGER_USER = {
  sub: '22222222-2222-2222-2222-222222222222',
  role: 'PASSENGER',
};
const DOCUMENT_ID = '33333333-3333-3333-3333-333333333333';

describe('DocumentsService', () => {
  let service: DocumentsService;
  let repository: jest.Mocked<DocumentsRepository>;
  let storageProvider: jest.Mocked<StorageProvider>;
  let runtimeConfig: jest.Mocked<RuntimeConfigService>;

  beforeEach(() => {
    repository = {
      create: jest.fn(),
      createApproved: jest.fn(),
      findById: jest.fn(),
      list: jest.fn(),
      approve: jest.fn(),
    } as unknown as jest.Mocked<DocumentsRepository>;
    storageProvider = {
      uploadObject: jest.fn(),
      downloadObject: jest.fn(),
      createSignedReadUrl: jest.fn(),
    };
    runtimeConfig = {
      getSnapshot: jest.fn().mockResolvedValue(makeRuntimeConfigSnapshot()),
    } as unknown as jest.Mocked<RuntimeConfigService>;
    service = new DocumentsService(repository, storageProvider, runtimeConfig);
  });

  it('uploads, auto-approves, requests ingest, and returns preview URL', async () => {
    repository.createApproved.mockResolvedValue(
      makeDocument({
        storagePath: 'documents/doc.txt',
        status: 'APPROVED',
        approvedByUserId: ADMIN_USER.sub,
        approvedAt: new Date('2026-06-13T00:00:00.000Z'),
      }),
    );
    storageProvider.createSignedReadUrl.mockResolvedValue('https://preview.example/doc.txt');

    const result = await service.create(
      {
        title: 'FAQ hành khách',
        accessLevel: 'PUBLIC',
        category: 'CUSTOMER_SUPPORT',
        documentType: 'FAQ',
        audienceRoles: ['PASSENGER'],
        language: 'vi',
      },
      makeFile('faq.txt', 'text/plain'),
      ADMIN_USER,
      'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
    );

    expect(storageProvider.uploadObject).toHaveBeenCalledWith(
      expect.objectContaining({ contentType: 'text/plain' }),
    );
    expect(repository.createApproved).toHaveBeenCalledWith(
      expect.objectContaining({
        accessLevel: 'PUBLIC',
        approvedByUserId: ADMIN_USER.sub,
        category: 'CUSTOMER_SUPPORT',
        fileType: 'TXT',
        uploadedByUserId: ADMIN_USER.sub,
      }),
    );
    expect(result.status).toBe('APPROVED');
    expect(result.previewUrl).toBe('https://preview.example/doc.txt');
  });

  it('rejects non-admin upload', async () => {
    await expect(
      service.create(
        {
          title: 'FAQ',
          accessLevel: 'PUBLIC',
          category: 'CUSTOMER_SUPPORT',
          documentType: 'FAQ',
          audienceRoles: [],
          language: 'vi',
        },
        makeFile('faq.txt', 'text/plain'),
        PASSENGER_USER,
        'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
      ),
    ).rejects.toBeInstanceOf(ForbiddenException);
  });

  it('lists documents for SYSTEM_ADMIN with pagination metadata', async () => {
    repository.list.mockResolvedValue({
      items: [makeDocument({ status: 'APPROVED' })],
      totalItems: 1,
    });

    const result = await service.list(
      {
        page: 1,
        pageSize: 20,
        sortBy: 'createdAt',
        sortDir: 'desc',
        status: 'APPROVED',
      },
      ADMIN_USER,
    );

    expect(repository.list).toHaveBeenCalledWith(
      expect.objectContaining({ page: 1, pageSize: 20, status: 'APPROVED' }),
    );
    expect(result.items).toHaveLength(1);
    expect(result.totalItems).toBe(1);
    expect(result.totalPages).toBe(1);
    expect(result.hasNextPage).toBe(false);
  });

  it('rejects non-admin document list', async () => {
    await expect(
      service.list(
        {
          page: 1,
          pageSize: 20,
          sortBy: 'createdAt',
          sortDir: 'desc',
        },
        PASSENGER_USER,
      ),
    ).rejects.toBeInstanceOf(ForbiddenException);
  });

  it('rejects invalid file type', async () => {
    await expect(
      service.create(
        {
          title: 'FAQ',
          accessLevel: 'PUBLIC',
          category: 'CUSTOMER_SUPPORT',
          documentType: 'FAQ',
          audienceRoles: [],
          language: 'vi',
        },
        makeFile('faq.pdf', 'application/pdf'),
        ADMIN_USER,
        'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
      ),
    ).rejects.toBeInstanceOf(BadRequestException);
  });

  it('approves a pending document and creates ingest outbox event', async () => {
    repository.findById.mockResolvedValue(makeDocument({ status: 'PENDING_REVIEW' }));
    repository.approve.mockResolvedValue(makeDocument({ status: 'APPROVED' }));

    const result = await service.approve(DOCUMENT_ID, ADMIN_USER);

    expect(repository.approve).toHaveBeenCalledWith({
      documentId: DOCUMENT_ID,
      approvedByUserId: ADMIN_USER.sub,
    });
    expect(result.status).toBe('APPROVED');
  });

  it('rejects approving missing document', async () => {
    repository.findById.mockResolvedValue(null);

    await expect(service.approve(DOCUMENT_ID, ADMIN_USER)).rejects.toBeInstanceOf(
      NotFoundException,
    );
  });

  it('rejects approving non-pending document', async () => {
    repository.findById.mockResolvedValue(makeDocument({ status: 'APPROVED' }));

    await expect(service.approve(DOCUMENT_ID, ADMIN_USER)).rejects.toBeInstanceOf(
      ConflictException,
    );
  });
});

function makeFile(originalname: string, mimetype: string): UploadedDocumentFile {
  return {
    originalname,
    mimetype,
    size: Buffer.byteLength('hello'),
    buffer: Buffer.from('hello'),
  };
}

function makeRuntimeConfigSnapshot(): RuntimeConfigSnapshot {
  return new RuntimeConfigSnapshot(
    new Map(RAG_RUNTIME_CONFIG_DEFINITIONS.map((definition) => [definition.key, definition.defaultValue])),
  );
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
    fileSize: BigInt(5),
    fileType: 'TXT',
    accessLevel: 'PUBLIC',
    category: 'CUSTOMER_SUPPORT',
    documentType: 'FAQ',
    audienceRoles: ['PASSENGER'],
    language: 'vi',
    operatorId: null,
    status: 'PENDING_REVIEW',
    ingestStatus: 'PENDING',
    ingestError: null,
    ingestedAt: null,
    chunkCount: null,
    embeddingModel: null,
    embeddingDimensions: null,
    uploadedByUserId: ADMIN_USER.sub,
    approvedByUserId: null,
    approvedAt: null,
    archivedAt: null,
    createdAt: new Date('2026-06-13T00:00:00.000Z'),
    updatedAt: new Date('2026-06-13T00:00:00.000Z'),
    ...overrides,
  };
}
