import { INestApplication } from '@nestjs/common';
import { APP_FILTER, APP_INTERCEPTOR } from '@nestjs/core';
import { Test, TestingModule } from '@nestjs/testing';
import {
  ApiResponseExceptionFilter,
  ApiResponseInterceptor,
  NestCommonModule,
} from '@vietride/nest-common';
import { NestRedisModule, RedisService } from '@vietride/nest-redis';
import { SignJWT } from 'jose';
import { createHash } from 'node:crypto';
import type { AddressInfo } from 'node:net';
import { CHAT_COMPLETION_PROVIDER, EMBEDDING_PROVIDER, STORAGE_PROVIDER } from '../app/tokens';
import { InternalJwtAuthGuard } from '../auth/internal-jwt-auth.guard';
import { ChatModule } from '../chat/chat.module';
import { RagConfigModule } from '../config/rag-config.module';
import { DocumentsModule } from '../documents/documents.module';
import type { ChatCompletionProvider } from '../providers/chat-completion.provider';
import type { EmbeddingProvider } from '../providers/embedding.provider';
import type { StorageProvider, UploadObjectRequest } from '../providers/storage.provider';
import { IngestRepository } from '../ingest/ingest.repository';
import { IngestModule } from '../ingest/ingest.module';
import { IngestService } from '../ingest/ingest.service';
import { RagPrismaService } from '../prisma/rag-prisma.service';
import { RagIdempotencyInterceptor } from './rag-idempotency.interceptor';
import { RagIdempotencyModule } from './rag-idempotency.module';

const describeReal = process.env.RAG_REAL_IDEMPOTENCY_E2E === '1' ? describe : describe.skip;
const INTERNAL_JWT_SECRET = process.env.INTERNAL_JWT_SECRET ?? '';
const ADMIN_USER_ID = '11111111-1111-4111-8111-111111111111';
const PASSENGER_USER_ID = '22222222-2222-4222-8222-222222222222';
const UPLOAD_OPERATION_ID = '33333333-3333-4333-8333-333333333333';
const CRASH_UPLOAD_OPERATION_ID = '44444444-4444-4444-8444-444444444444';
const CHAT_OPERATION_ID = '55555555-5555-4555-8555-555555555555';
const LEGACY_PG_OPERATION_ID = '66666666-6666-4666-8666-666666666666';
const LEGACY_REDIS_OPERATION_ID = '77777777-7777-4777-8777-777777777777';
const EMBEDDING_DIMENSIONS = 3_072;

describeReal('RAG idempotency with real PostgreSQL and Redis (system e2e)', () => {
  let app: INestApplication;
  let moduleRef: TestingModule;
  let baseUrl: string;
  let prisma: RagPrismaService;
  let redis: RedisService;
  let ingestService: IngestService;
  let ingestRepository: IngestRepository;
  let uploadedDocumentId: string;
  let storage: DeterministicStorageProvider;
  let chat: DeterministicChatProvider;

  beforeAll(async () => {
    if (!INTERNAL_JWT_SECRET) throw new Error('INTERNAL_JWT_SECRET is required');
    storage = new DeterministicStorageProvider();
    chat = new DeterministicChatProvider();

    moduleRef = await Test.createTestingModule({
      imports: [
        NestCommonModule,
        RagConfigModule,
        NestRedisModule.forRoot({ url: process.env.REDIS_URL ?? '' }),
        DocumentsModule,
        IngestModule,
        ChatModule,
        RagIdempotencyModule,
      ],
      providers: [
        InternalJwtAuthGuard,
        { provide: APP_FILTER, useValue: new ApiResponseExceptionFilter() },
        { provide: APP_INTERCEPTOR, useValue: new ApiResponseInterceptor() },
        { provide: APP_INTERCEPTOR, useClass: RagIdempotencyInterceptor },
      ],
    })
      .overrideProvider(STORAGE_PROVIDER)
      .useValue(storage)
      .overrideProvider(EMBEDDING_PROVIDER)
      .useValue(new DeterministicEmbeddingProvider())
      .overrideProvider(CHAT_COMPLETION_PROVIDER)
      .useValue(chat)
      .compile();

    app = moduleRef.createNestApplication();
    await app.listen(0, '127.0.0.1');
    baseUrl = `http://127.0.0.1:${(app.getHttpServer().address() as AddressInfo).port}`;
    prisma = app.get(RagPrismaService);
    redis = app.get(RedisService);
    ingestService = app.get(IngestService);
    ingestRepository = app.get(IngestRepository);

    await redis.getClient().flushdb();
    await prisma.idempotencyOperation.deleteMany({
      where: {
        operationId: {
          in: [
            UPLOAD_OPERATION_ID,
            CRASH_UPLOAD_OPERATION_ID,
            CHAT_OPERATION_ID,
            LEGACY_PG_OPERATION_ID,
            LEGACY_REDIS_OPERATION_ID,
          ],
        },
      },
    });
  }, 60_000);

  afterAll(async () => {
    await app?.close();
  });

  it('binds multipart file bytes into the fingerprint and replays without duplicate storage, row, or outbox effects', async () => {
    const token = await signInternalJwt(ADMIN_USER_ID, 'SYSTEM_ADMIN');
    const original = await uploadDocument(
      baseUrl,
      token,
      UPLOAD_OPERATION_ID,
      Buffer.from('# Quy trinh\nNoi dung ban dau', 'utf8'),
    );
    const replay = await uploadDocument(
      baseUrl,
      token,
      UPLOAD_OPERATION_ID,
      Buffer.from('# Quy trinh\nNoi dung ban dau', 'utf8'),
    );
    const mismatch = await uploadDocument(
      baseUrl,
      token,
      UPLOAD_OPERATION_ID,
      Buffer.from('# Quy trinh\nNoi dung da bi thay doi', 'utf8'),
    );

    expect(original.response.status).toBe(201);
    expect(replay.response.status).toBe(201);
    expect(mismatch.response.status).toBe(422);
    expect(readErrorCode(mismatch.json)).toBe('IDEMPOTENCY_KEY_MISMATCH');
    uploadedDocumentId = readDataId(original.json);
    expect(readDataId(replay.json)).toBe(uploadedDocumentId);
    expect(storage.uploadCalls).toBe(1);
    expect(await prisma.knowledgeDocument.count({ where: { id: uploadedDocumentId } })).toBe(1);
    expect(await countOutboxEventsForDocument(prisma, uploadedDocumentId)).toBe(1);
    expect(await redis.get(idempotencyResponseKey(UPLOAD_OPERATION_ID))).not.toBeNull();
  });

  it('rejects a non-v4 key before running the upload', async () => {
    const token = await signInternalJwt(ADMIN_USER_ID, 'SYSTEM_ADMIN');
    const result = await uploadDocument(
      baseUrl,
      token,
      'not-a-uuid',
      Buffer.from('invalid key must not upload', 'utf8'),
    );

    expect(result.response.status).toBe(422);
    expect(storage.uploadCalls).toBe(1);
    expect(await prisma.knowledgeDocument.count({ where: { id: uploadedDocumentId } })).toBe(1);
  });

  it('fails closed across rollout for both pre-v2 PostgreSQL and Redis keys', async () => {
    const uploadCallsBefore = storage.uploadCalls;
    await prisma.idempotencyOperation.create({
      data: {
        operationId: LEGACY_PG_OPERATION_ID,
        userId: ADMIN_USER_ID,
        method: 'POST',
        path: '/v1/rag/documents',
        fingerprint: 'A'.repeat(64),
        ownerToken: '88888888-8888-4888-8888-888888888888',
        status: 'COMPLETED',
        expiresAt: new Date(Date.now() + 60_000),
      },
    });
    await redis
      .getClient()
      .set(`rag:idem:${LEGACY_REDIS_OPERATION_ID}`, 'legacy-body-hash', 'EX', 86_400);
    const token = await signInternalJwt(ADMIN_USER_ID, 'SYSTEM_ADMIN');

    const postgresLegacy = await uploadDocument(
      baseUrl,
      token,
      LEGACY_PG_OPERATION_ID,
      Buffer.from('legacy postgres retry', 'utf8'),
    );
    const redisLegacy = await uploadDocument(
      baseUrl,
      token,
      LEGACY_REDIS_OPERATION_ID,
      Buffer.from('legacy redis retry', 'utf8'),
    );

    expect(postgresLegacy.response.status).toBe(422);
    expect(readErrorCode(postgresLegacy.json)).toBe('IDEMPOTENCY_KEY_MISMATCH');
    expect(redisLegacy.response.status).toBe(422);
    expect(readErrorCode(redisLegacy.json)).toBe('IDEMPOTENCY_KEY_MISMATCH');
    expect(storage.uploadCalls).toBe(uploadCallsBefore);
  });

  it('ingests once and a durable retry does not create duplicate chunks or outbox events', async () => {
    expect(await ingestService.processPendingOnce(5)).toBe(1);
    const firstChunks = await prisma.knowledgeChunk.findMany({
      where: { documentId: uploadedDocumentId },
      orderBy: { chunkIndex: 'asc' },
    });
    const event = await findOutboxEvent(prisma, uploadedDocumentId);
    expect(firstChunks.length).toBeGreaterThan(0);
    expect(event.status).toBe('PUBLISHED');

    await prisma.outboxEvent.update({
      where: { id: event.id },
      data: { status: 'FAILED' },
    });
    expect(await ingestService.processPendingOnce(5)).toBe(1);
    expect(await prisma.knowledgeChunk.count({ where: { documentId: uploadedDocumentId } })).toBe(
      firstChunks.length,
    );
    expect(await countOutboxEventsForDocument(prisma, uploadedDocumentId)).toBe(1);
  });

  it('recovers a crash after the ingest DB commit but before the Redis marker and outbox publish', async () => {
    const token = await signInternalJwt(ADMIN_USER_ID, 'SYSTEM_ADMIN');
    const upload = await uploadDocument(
      baseUrl,
      token,
      CRASH_UPLOAD_OPERATION_ID,
      Buffer.from('# Crash recovery\nChi mot chunk ben vung', 'utf8'),
      'Crash recovery document',
    );
    expect(upload.response.status).toBe(201);
    const documentId = readDataId(upload.json);
    const document = await prisma.knowledgeDocument.findUniqueOrThrow({
      where: { id: documentId },
    });
    const event = await findOutboxEvent(prisma, documentId);

    expect(await ingestRepository.markDocumentProcessing(documentId)).toBe(true);
    await ingestRepository.replaceChunksAndComplete(
      document,
      [
        {
          chunkIndex: 0,
          sectionHeader: 'Crash recovery',
          content: 'Chi mot chunk ben vung',
          tokenCount: 5,
          embedding: deterministicEmbedding(),
        },
      ],
      'e2e-embedding',
      EMBEDDING_DIMENSIONS,
    );
    await prisma.outboxEvent.update({
      where: { id: event.id },
      data: { status: 'PUBLISHING' },
    });

    expect(await ingestService.processPendingOnce(5)).toBe(1);
    expect(await prisma.knowledgeChunk.count({ where: { documentId } })).toBe(1);
    expect((await prisma.outboxEvent.findUniqueOrThrow({ where: { id: event.id } })).status).toBe(
      'PUBLISHED',
    );
    expect(
      (await prisma.outboxEvent.findMany()).filter(
        (item) => readPayloadDocumentId(item.payload) === documentId,
      ),
    ).toHaveLength(1);
  });

  it('finishes an aborted SSE operation and reconnects by replaying it without duplicate chat side effects', async () => {
    const conversationsBefore = await prisma.ragConversation.count({
      where: { userId: PASSENGER_USER_ID },
    });
    const messagesBefore = await prisma.ragMessage.count({
      where: { conversation: { userId: PASSENGER_USER_ID } },
    });
    const token = await signInternalJwt(PASSENGER_USER_ID, 'PASSENGER');
    const payload = JSON.stringify({ message: 'Toi can huong dan dat ve' });
    const controller = new AbortController();
    const first = await fetch(`${baseUrl}/v1/rag/chat`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Internal-Auth': token,
        'Idempotency-Key': CHAT_OPERATION_ID,
      },
      body: payload,
      signal: controller.signal,
    });
    expect(first.status).toBe(200);
    const reader = first.body?.getReader();
    if (!reader) throw new Error('SSE response body is missing');
    const firstChunk = await reader.read();
    expect(new TextDecoder().decode(firstChunk.value)).toContain('event: token');
    controller.abort();

    await waitFor(async () => {
      return (await redis.get(idempotencyResponseKey(CHAT_OPERATION_ID))) !== null;
    });

    const replay = await fetch(`${baseUrl}/v1/rag/chat`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Internal-Auth': token,
        'Idempotency-Key': CHAT_OPERATION_ID,
      },
      body: payload,
    });
    const replayBody = await replay.text();
    expect(replay.status).toBe(200);
    expect(replayBody).toContain('event: done');
    expect(chat.streamCalls).toBe(1);
    expect(await prisma.ragConversation.count({ where: { userId: PASSENGER_USER_ID } })).toBe(
      conversationsBefore + 1,
    );
    expect(
      await prisma.ragMessage.count({
        where: { conversation: { userId: PASSENGER_USER_ID } },
      }),
    ).toBe(messagesBefore + 2);

    const mismatch = await fetch(`${baseUrl}/v1/rag/chat`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Internal-Auth': token,
        'Idempotency-Key': CHAT_OPERATION_ID,
      },
      body: JSON.stringify({ message: 'Payload khac' }),
    });
    expect(mismatch.status).toBe(422);
    expect(readErrorCode(await mismatch.json())).toBe('IDEMPOTENCY_KEY_MISMATCH');
  });
});

class DeterministicStorageProvider implements StorageProvider {
  readonly objects = new Map<string, Buffer>();
  uploadCalls = 0;

  async uploadObject(request: UploadObjectRequest): Promise<void> {
    this.uploadCalls += 1;
    this.objects.set(request.storagePath, Buffer.from(request.body));
  }

  async downloadObject(storagePath: string): Promise<Buffer> {
    const value = this.objects.get(storagePath);
    if (!value) throw new Error(`Missing deterministic storage object ${storagePath}`);
    return Buffer.from(value);
  }

  async createSignedReadUrl(request: { storagePath: string }): Promise<string> {
    return `https://storage.invalid/${encodeURIComponent(request.storagePath)}`;
  }
}

class DeterministicEmbeddingProvider implements EmbeddingProvider {
  async embed(): Promise<number[]> {
    return deterministicEmbedding();
  }
}

class DeterministicChatProvider implements ChatCompletionProvider {
  streamCalls = 0;

  async complete(): Promise<string> {
    return 'deterministic completion';
  }

  async *stream(): AsyncIterable<string> {
    this.streamCalls += 1;
    yield 'Huong dan ';
    await new Promise((resolve) => setTimeout(resolve, 150));
    yield 'dat ve';
  }
}

function deterministicEmbedding(): number[] {
  return Array.from({ length: EMBEDDING_DIMENSIONS }, (_, index) => (index + 1) / 1_000_000);
}

async function uploadDocument(
  baseUrl: string,
  token: string,
  operationId: string,
  file: Buffer,
  title = 'Tai lieu idempotency',
): Promise<{ response: Response; json: unknown }> {
  const form = new FormData();
  form.set('file', new Blob([file], { type: 'text/plain' }), 'guide.txt');
  form.set('title', title);
  form.set('accessLevel', 'PUBLIC');
  form.set('category', 'CUSTOMER_SUPPORT');
  form.set('documentType', 'GUIDE');
  form.set('audienceRoles', 'PASSENGER');
  form.set('language', 'vi');
  const response = await fetch(`${baseUrl}/v1/rag/documents`, {
    method: 'POST',
    headers: {
      'X-Internal-Auth': token,
      'Idempotency-Key': operationId,
    },
    body: form,
  });
  return { response, json: await response.json() };
}

async function signInternalJwt(userId: string, role: string): Promise<string> {
  const token = await new SignJWT({ sub: userId, role })
    .setProtectedHeader({ alg: 'HS256', typ: 'JWT' })
    .setIssuer('vietride-gateway')
    .setAudience('vietride-internal')
    .setIssuedAt()
    .setExpirationTime('120s')
    .sign(new TextEncoder().encode(INTERNAL_JWT_SECRET));
  return `Bearer ${token}`;
}

function readDataId(value: unknown): string {
  const envelope = value as { data?: { id?: unknown } };
  if (typeof envelope.data?.id !== 'string') {
    throw new Error(`Response does not contain data.id: ${JSON.stringify(value)}`);
  }
  return envelope.data.id;
}

function readErrorCode(value: unknown): string | undefined {
  const envelope = value as { error?: { code?: unknown }; errorCode?: unknown };
  if (typeof envelope.error?.code === 'string') return envelope.error.code;
  return typeof envelope.errorCode === 'string' ? envelope.errorCode : undefined;
}

async function waitFor(predicate: () => Promise<boolean>, timeoutMs = 5_000): Promise<void> {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (await predicate()) return;
    await new Promise((resolve) => setTimeout(resolve, 50));
  }
  throw new Error(`Condition was not met within ${timeoutMs}ms`);
}

async function findOutboxEvent(prisma: RagPrismaService, documentId: string) {
  const event = (await prisma.outboxEvent.findMany()).find(
    (item) => readPayloadDocumentId(item.payload) === documentId,
  );
  if (!event) throw new Error(`Outbox event for document ${documentId} was not found`);
  return event;
}

async function countOutboxEventsForDocument(
  prisma: RagPrismaService,
  documentId: string,
): Promise<number> {
  return (await prisma.outboxEvent.findMany()).filter(
    (item) => readPayloadDocumentId(item.payload) === documentId,
  ).length;
}

function readPayloadDocumentId(payload: unknown): string | undefined {
  if (!payload || typeof payload !== 'object' || Array.isArray(payload)) return undefined;
  const value = (payload as { documentId?: unknown }).documentId;
  return typeof value === 'string' ? value : undefined;
}

function idempotencyResponseKey(operationId: string): string {
  const hash = createHash('sha256')
    .update(operationId.toLowerCase(), 'utf8')
    .digest('hex')
    .toUpperCase();
  return `rag:idem:v2:response:${hash}`;
}
