import { ForbiddenException, INestApplication } from '@nestjs/common';
import { Test, TestingModule } from '@nestjs/testing';
import { SignJWT } from 'jose';
import type { AddressInfo } from 'node:net';
import { ENV_TOKEN, STORAGE_PROVIDER } from '../app/tokens';
import { InternalJwtAuthGuard } from '../auth/internal-jwt-auth.guard';
import { RAG_RUNTIME_CONFIG_DEFINITIONS } from '../config/runtime-config.registry';
import { RuntimeConfigService, RuntimeConfigSnapshot } from '../config/runtime-config.service';
import type { KnowledgeDocument } from '../generated/rag-prisma-client';
import type { StorageProvider } from '../providers/storage.provider';
import { DocumentsController } from './documents.controller';
import { DocumentsRepository } from './documents.repository';
import { DocumentsService } from './documents.service';

const INTERNAL_JWT_SECRET = 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa';
const INTERNAL_JWT_ISSUER = 'vietride-gateway';
const INTERNAL_JWT_AUDIENCE = 'vietride-internal';
const ADMIN_USER_ID = '11111111-1111-1111-1111-111111111111';
const PASSENGER_USER_ID = '22222222-2222-2222-2222-222222222222';
const DOCUMENT_ID = '33333333-3333-3333-3333-333333333333';

describe('DocumentsController (e2e)', () => {
  let app: INestApplication;
  let baseUrl: string;
  let repository: jest.Mocked<DocumentsRepository>;
  let storageProvider: jest.Mocked<StorageProvider>;
  let runtimeConfig: jest.Mocked<RuntimeConfigService>;

  beforeAll(async () => {
    repository = {
      create: jest.fn(),
      findById: jest.fn(),
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

    const moduleFixture: TestingModule = await Test.createTestingModule({
      controllers: [DocumentsController],
      providers: [
        DocumentsService,
        InternalJwtAuthGuard,
        { provide: DocumentsRepository, useValue: repository },
        { provide: STORAGE_PROVIDER, useValue: storageProvider },
        { provide: ENV_TOKEN, useValue: { INTERNAL_JWT_SECRET } },
        { provide: RuntimeConfigService, useValue: runtimeConfig },
      ],
    }).compile();

    app = moduleFixture.createNestApplication();
    app.setGlobalPrefix('api');
    await app.listen(0);
    const address = app.getHttpServer().address() as AddressInfo;
    baseUrl = `http://127.0.0.1:${address.port}`;
  });

  afterAll(async () => {
    await app.close();
  });

  beforeEach(() => {
    jest.clearAllMocks();
    repository.create.mockResolvedValue(makeDocument());
    repository.findById.mockResolvedValue(makeDocument({ status: 'PENDING_REVIEW' }));
    repository.approve.mockResolvedValue(
      makeDocument({
        status: 'APPROVED',
        approvedByUserId: ADMIN_USER_ID,
        approvedAt: new Date('2026-06-13T00:00:00.000Z'),
      }),
    );
    storageProvider.createSignedReadUrl.mockResolvedValue('https://preview.example/faq.txt');
  });

  it('POST /api/v1/rag/documents returns 201 for SYSTEM_ADMIN TXT upload', async () => {
    const response = await fetch(`${baseUrl}/api/v1/rag/documents`, {
      method: 'POST',
      headers: { 'X-Internal-Auth': await signInternalJwt(ADMIN_USER_ID, 'SYSTEM_ADMIN') },
      body: makeValidForm(),
    });
    const body = (await response.json()) as { id?: string; previewUrl?: string };

    expect(response.status).toBe(201);
    expect(body.id).toBe(DOCUMENT_ID);
    expect(body.previewUrl).toBe('https://preview.example/faq.txt');
  });

  it('POST /api/v1/rag/documents returns 401 without internal JWT', async () => {
    const response = await fetch(`${baseUrl}/api/v1/rag/documents`, {
      method: 'POST',
      body: makeValidForm(),
    });

    expect(response.status).toBe(401);
  });

  it('POST /api/v1/rag/documents returns 403 for non-admin caller', async () => {
    const response = await fetch(`${baseUrl}/api/v1/rag/documents`, {
      method: 'POST',
      headers: { 'X-Internal-Auth': await signInternalJwt(PASSENGER_USER_ID, 'PASSENGER') },
      body: makeValidForm(),
    });

    expect(response.status).toBe(403);
  });

  it('POST /api/v1/rag/documents returns 400 for invalid file', async () => {
    const form = makeValidForm();
    form.set('file', new Blob(['bad'], { type: 'application/pdf' }), 'bad.pdf');

    const response = await fetch(`${baseUrl}/api/v1/rag/documents`, {
      method: 'POST',
      headers: { 'X-Internal-Auth': await signInternalJwt(ADMIN_USER_ID, 'SYSTEM_ADMIN') },
      body: form,
    });

    expect(response.status).toBe(400);
  });

  it('PUT /api/v1/rag/documents/{documentId}/approve returns 200 for SYSTEM_ADMIN', async () => {
    const response = await fetch(`${baseUrl}/api/v1/rag/documents/${DOCUMENT_ID}/approve`, {
      method: 'PUT',
      headers: { 'X-Internal-Auth': await signInternalJwt(ADMIN_USER_ID, 'SYSTEM_ADMIN') },
    });
    const body = (await response.json()) as { status?: string };

    expect(response.status).toBe(200);
    expect(body.status).toBe('APPROVED');
  });

  it('PUT /api/v1/rag/documents/{documentId}/approve returns 403 for non-admin caller', async () => {
    jest.spyOn(DocumentsService.prototype, 'approve').mockRejectedValueOnce(
      new ForbiddenException({
        errorCode: 'INSUFFICIENT_ROLE',
        detail: 'SYSTEM_ADMIN role is required',
      }),
    );

    const response = await fetch(`${baseUrl}/api/v1/rag/documents/${DOCUMENT_ID}/approve`, {
      method: 'PUT',
      headers: { 'X-Internal-Auth': await signInternalJwt(PASSENGER_USER_ID, 'PASSENGER') },
    });

    expect(response.status).toBe(403);
  });
});

function makeValidForm(): FormData {
  const form = new FormData();
  form.set('file', new Blob(['Xin chào'], { type: 'text/plain' }), 'faq.txt');
  form.set('title', 'FAQ hành khách');
  form.set('accessLevel', 'PUBLIC');
  form.set('category', 'CUSTOMER_SUPPORT');
  form.set('documentType', 'FAQ');
  form.set('audienceRoles', 'PASSENGER');
  form.set('language', 'vi');
  return form;
}

function makeRuntimeConfigSnapshot(): RuntimeConfigSnapshot {
  return new RuntimeConfigSnapshot(
    new Map(RAG_RUNTIME_CONFIG_DEFINITIONS.map((definition) => [definition.key, definition.defaultValue])),
  );
}

async function signInternalJwt(sub: string, role: string): Promise<string> {
  const token = await new SignJWT({ sub, role, reqId: 'req-rag-phase3' })
    .setProtectedHeader({ alg: 'HS256', typ: 'JWT' })
    .setIssuer(INTERNAL_JWT_ISSUER)
    .setAudience(INTERNAL_JWT_AUDIENCE)
    .setIssuedAt()
    .setExpirationTime('120s')
    .sign(new TextEncoder().encode(INTERNAL_JWT_SECRET));

  return `Bearer ${token}`;
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
    fileSize: BigInt(8),
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
    uploadedByUserId: ADMIN_USER_ID,
    approvedByUserId: null,
    approvedAt: null,
    archivedAt: null,
    createdAt: new Date('2026-06-13T00:00:00.000Z'),
    updatedAt: new Date('2026-06-13T00:00:00.000Z'),
    ...overrides,
  };
}
