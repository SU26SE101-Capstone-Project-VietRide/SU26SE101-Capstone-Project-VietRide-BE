import { HttpException, HttpStatus, INestApplication } from '@nestjs/common';
import { Test, TestingModule } from '@nestjs/testing';
import { SignJWT } from 'jose';
import type { AddressInfo } from 'node:net';
import { CHAT_COMPLETION_PROVIDER, EMBEDDING_PROVIDER, ENV_TOKEN } from '../app/tokens';
import { InternalJwtAuthGuard } from '../auth/internal-jwt-auth.guard';
import { RAG_RUNTIME_CONFIG_DEFINITIONS } from '../config/runtime-config.registry';
import { RuntimeConfigService, RuntimeConfigSnapshot } from '../config/runtime-config.service';
import type { MessageFeedback, RagConversation, RagMessage } from '../generated/rag-prisma-client';
import type { ChatCompletionProvider } from '../providers/chat-completion.provider';
import type { EmbeddingProvider } from '../providers/embedding.provider';
import { ChatEmbeddingCacheService } from './chat-embedding-cache.service';
import { ChatIntentService } from './chat-intent.service';
import { ChatQueryRewriteService } from './chat-query-rewrite.service';
import { ChatRateLimitService } from './chat-rate-limit.service';
import { ChatRerankService } from './chat-rerank.service';
import { ChatController } from './chat.controller';
import { ChatRepository } from './chat.repository';
import { ChatService } from './chat.service';
import { ChatSummaryService } from './chat-summary.service';
import { FeedbackController } from './feedback.controller';
import { FeedbackService } from './feedback.service';

const INTERNAL_JWT_SECRET = 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa';
const INTERNAL_JWT_ISSUER = 'vietride-gateway';
const INTERNAL_JWT_AUDIENCE = 'vietride-internal';
const USER_ID = '11111111-1111-1111-1111-111111111111';
const OTHER_USER_ID = '99999999-9999-9999-9999-999999999999';
const OPERATOR_ID = '22222222-2222-2222-2222-222222222222';
const CONVERSATION_ID = '33333333-3333-3333-3333-333333333333';
const USER_MESSAGE_ID = '44444444-4444-4444-4444-444444444444';
const ASSISTANT_MESSAGE_ID = '55555555-5555-5555-5555-555555555555';
const CHUNK_ID = '66666666-6666-6666-6666-666666666666';

describe('ChatController (e2e)', () => {
  let app: INestApplication;
  let baseUrl: string;
  let repository: jest.Mocked<ChatRepository>;
  let embeddingCache: jest.Mocked<ChatEmbeddingCacheService>;
  let rateLimit: jest.Mocked<ChatRateLimitService>;
  let rerankService: jest.Mocked<ChatRerankService>;
  let chatProvider: jest.Mocked<ChatCompletionProvider>;
  let embeddingProvider: jest.Mocked<EmbeddingProvider>;
  let runtimeConfig: jest.Mocked<RuntimeConfigService>;

  beforeAll(async () => {
    repository = {
      findConversation: jest.fn(),
      createConversation: jest.fn(),
      createUserMessage: jest.fn(),
      createAssistantMessage: jest.fn(),
      findRecentMessages: jest.fn(),
      countMessages: jest.fn(),
      updateConversationSummary: jest.fn(),
      findMessageWithConversation: jest.fn(),
      upsertMessageFeedback: jest.fn(),
      listFeedback: jest.fn(),
      searchChunks: jest.fn(),
    } as unknown as jest.Mocked<ChatRepository>;
    embeddingCache = {
      get: jest.fn(),
      set: jest.fn(),
    } as unknown as jest.Mocked<ChatEmbeddingCacheService>;
    rateLimit = {
      assertAllowed: jest.fn(),
    } as unknown as jest.Mocked<ChatRateLimitService>;
    rerankService = {
      rerank: jest.fn(),
    } as unknown as jest.Mocked<ChatRerankService>;
    chatProvider = {
      complete: jest.fn(),
      stream: jest.fn(),
    };
    embeddingProvider = {
      embed: jest.fn(),
    };
    runtimeConfig = {
      getSnapshot: jest.fn().mockResolvedValue(makeRuntimeConfigSnapshot()),
    } as unknown as jest.Mocked<RuntimeConfigService>;

    const moduleFixture: TestingModule = await Test.createTestingModule({
      controllers: [ChatController, FeedbackController],
      providers: [
        ChatService,
        FeedbackService,
        ChatIntentService,
        ChatQueryRewriteService,
        ChatSummaryService,
        InternalJwtAuthGuard,
        { provide: ChatRepository, useValue: repository },
        { provide: ChatEmbeddingCacheService, useValue: embeddingCache },
        { provide: ChatRateLimitService, useValue: rateLimit },
        { provide: ChatRerankService, useValue: rerankService },
        { provide: CHAT_COMPLETION_PROVIDER, useValue: chatProvider },
        { provide: EMBEDDING_PROVIDER, useValue: embeddingProvider },
        { provide: ENV_TOKEN, useValue: makeEnv() },
        { provide: RuntimeConfigService, useValue: runtimeConfig },
      ],
    }).compile();

    app = moduleFixture.createNestApplication();
        await app.listen(0);
    const address = app.getHttpServer().address() as AddressInfo;
    baseUrl = `http://127.0.0.1:${address.port}`;
  });

  afterAll(async () => {
    await app.close();
  });

  beforeEach(() => {
    jest.clearAllMocks();
    repository.createConversation.mockResolvedValue(makeConversation());
    repository.createUserMessage.mockResolvedValue(makeMessage('USER', 'Tôi cần hỗ trợ'));
    repository.createAssistantMessage.mockResolvedValue(makeMessage('ASSISTANT', 'Xin chào'));
    repository.findRecentMessages.mockResolvedValue([]);
    repository.searchChunks.mockResolvedValue([makeChunk()]);
    repository.findMessageWithConversation.mockResolvedValue(makeMessageWithConversation('ASSISTANT'));
    repository.upsertMessageFeedback.mockResolvedValue(makeFeedback());
    repository.listFeedback.mockResolvedValue({
      items: [],
      page: 1,
      pageSize: 20,
      totalItems: 0,
      totalPages: 0,
      hasNextPage: false,
      hasPreviousPage: false,
    });
    rateLimit.assertAllowed.mockResolvedValue(undefined);
    rerankService.rerank.mockImplementation(async (_query, chunks) => chunks.slice(0, 5));
    embeddingCache.get.mockResolvedValue(undefined);
    embeddingProvider.embed.mockResolvedValue([0.1, 0.2]);
    chatProvider.stream.mockReturnValue(makeTokenStream(['Xin ', 'chào']));
  });

  it('POST /v1/rag/chat streams token and done events', async () => {
    const response = await fetch(`${baseUrl}/v1/rag/chat`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Internal-Auth': await signInternalJwt(USER_ID, 'PASSENGER'),
      },
      body: JSON.stringify({ message: 'Tôi cần hỗ trợ' }),
    });
    const body = await response.text();

    expect(response.status).toBe(200);
    expect(response.headers.get('content-type')).toContain('text/event-stream');
    expect(body).toContain('event: token');
    expect(body).toContain('event: done');
    expect(body).toContain(CHUNK_ID);
    expect(repository.searchChunks).toHaveBeenCalledWith(
      expect.objectContaining({ accessLevels: ['PUBLIC'] }),
    );
  });

  it('POST /v1/rag/chat returns 401 without internal JWT', async () => {
    const response = await fetch(`${baseUrl}/v1/rag/chat`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ message: 'Tôi cần hỗ trợ' }),
    });

    expect(response.status).toBe(401);
  });

  it('POST /v1/rag/chat returns 400 for invalid payload', async () => {
    const response = await fetch(`${baseUrl}/v1/rag/chat`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Internal-Auth': await signInternalJwt(USER_ID, 'PASSENGER'),
      },
      body: JSON.stringify({ message: '' }),
    });

    expect(response.status).toBe(400);
  });

  it('POST /v1/rag/chat returns 429 when rate limit is exceeded', async () => {
    rateLimit.assertAllowed.mockRejectedValue(
      new HttpException(
        {
          errorCode: 'RAG_RATE_LIMIT_EXCEEDED',
          detail: 'RAG chat rate limit exceeded',
        },
        HttpStatus.TOO_MANY_REQUESTS,
      ),
    );

    const response = await fetch(`${baseUrl}/v1/rag/chat`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Internal-Auth': await signInternalJwt(USER_ID, 'PASSENGER'),
      },
      body: JSON.stringify({ message: 'Tôi cần hỗ trợ' }),
    });

    expect(response.status).toBe(429);
  });

  it('POST /v1/rag/chat refuses off-topic messages when intent filter is enabled', async () => {
    const response = await fetch(`${baseUrl}/v1/rag/chat`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Internal-Auth': await signInternalJwt(USER_ID, 'PASSENGER'),
      },
      body: JSON.stringify({ message: 'Viết thơ về chứng khoán' }),
    });
    const body = await response.text();

    expect(response.status).toBe(200);
    expect(body).toContain('event: token');
    expect(body).toContain('event: done');
    expect(repository.searchChunks).not.toHaveBeenCalled();
  });

  it('POST /v1/rag/messages/:id/feedback records assistant feedback', async () => {
    const response = await fetch(`${baseUrl}/v1/rag/messages/${ASSISTANT_MESSAGE_ID}/feedback`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Internal-Auth': await signInternalJwt(USER_ID, 'PASSENGER'),
      },
      body: JSON.stringify({ rating: 1 }),
    });
    const body = (await response.json()) as { rating: number };

    expect(response.status).toBe(201);
    expect(body.rating).toBe(1);
    expect(repository.upsertMessageFeedback).toHaveBeenCalled();
  });

  it('POST /v1/rag/messages/:id/feedback returns 401 without internal JWT', async () => {
    const response = await fetch(`${baseUrl}/v1/rag/messages/${ASSISTANT_MESSAGE_ID}/feedback`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ rating: 1 }),
    });

    expect(response.status).toBe(401);
  });

  it('POST /v1/rag/messages/:id/feedback returns 400 for invalid payload', async () => {
    const response = await fetch(`${baseUrl}/v1/rag/messages/${ASSISTANT_MESSAGE_ID}/feedback`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Internal-Auth': await signInternalJwt(USER_ID, 'PASSENGER'),
      },
      body: JSON.stringify({ rating: 0 }),
    });

    expect(response.status).toBe(400);
  });

  it('POST /v1/rag/messages/:id/feedback returns 403 for non-owner feedback', async () => {
    const response = await fetch(`${baseUrl}/v1/rag/messages/${ASSISTANT_MESSAGE_ID}/feedback`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Internal-Auth': await signInternalJwt(OTHER_USER_ID, 'PASSENGER'),
      },
      body: JSON.stringify({ rating: -1 }),
    });

    expect(response.status).toBe(403);
  });

  it('POST /v1/rag/chat uses operatorId scope for SYSTEM_ADMIN', async () => {
    repository.createConversation.mockResolvedValue(makeConversation({ operatorId: OPERATOR_ID }));

    const response = await fetch(`${baseUrl}/v1/rag/chat`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Internal-Auth': await signInternalJwt(USER_ID, 'SYSTEM_ADMIN'),
      },
      body: JSON.stringify({ message: 'Admin scope test', operatorId: OPERATOR_ID }),
    });

    expect(response.status).toBe(200);
    expect(repository.searchChunks).toHaveBeenCalledWith(
      expect.objectContaining({
        operatorId: OPERATOR_ID,
        callerRole: 'SYSTEM_ADMIN',
      }),
    );
  });

  it('POST /v1/rag/chat returns 403 for non-admin sending operatorId', async () => {
    const response = await fetch(`${baseUrl}/v1/rag/chat`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Internal-Auth': await signInternalJwt(USER_ID, 'PASSENGER'),
      },
      body: JSON.stringify({ message: 'Test', operatorId: OPERATOR_ID }),
    });

    expect(response.status).toBe(403);
  });

  it('POST /v1/rag/messages/:id/feedback returns 403 when SYSTEM_ADMIN feedbacks non-owner message', async () => {
    const response = await fetch(`${baseUrl}/v1/rag/messages/${ASSISTANT_MESSAGE_ID}/feedback`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Internal-Auth': await signInternalJwt(OTHER_USER_ID, 'SYSTEM_ADMIN'),
      },
      body: JSON.stringify({ rating: 1 }),
    });

    expect(response.status).toBe(403);
  });
});

function makeEnv() {
  return {
    NODE_ENV: 'test',
    PORT: 3003,
    GATEWAY_URL: 'http://gateway:3000',
    DATABASE_URL: 'postgresql://user:pass@localhost:5432/vietride_rag',
    REDIS_URL: 'redis://localhost:6379',
    REDIS_HOST: 'localhost',
    REDIS_PORT: 6379,
    RABBITMQ_URL: 'amqp://guest:guest@localhost:5672',
    RABBITMQ_EXCHANGE: 'vietride.events',
    INTERNAL_JWT_SECRET,
    INTERNAL_JWT_TTL_SEC: 120,
    JWT_ISSUER: 'vietride-identity',
    JWT_AUDIENCE: 'vietride-api',
    LOG_LEVEL: 'info',
    OPENROUTER_API_KEY: 'test-key',
    OPENROUTER_BASE_URL: 'https://openrouter.ai/v1',
    OPENROUTER_CHAT_MODEL: 'nvidia/nemotron-3-ultra-550b-a55b:free',
    OPENROUTER_EMBEDDING_MODEL: 'nvidia/llama-nemotron-embed-vl-1b-v2:free',
    OPENROUTER_HTTP_REFERER: undefined,
    OPENROUTER_APP_TITLE: 'VietRide RAG',
    OPENROUTER_ALLOW_PAID_FALLBACK: false,
    RAG_EMBEDDING_DIMENSIONS: 'auto',
    RAG_PROVIDER_TIMEOUT_MS: 10_000,
    RAG_MAX_MESSAGE_CHARS: 500,
    RAG_MAX_CONTEXT_TOKENS: 4_000,
    RAG_MAX_RETRIEVED_CHUNKS: 5,
    RAG_USER_RATE_LIMIT_PER_HOUR: 20,
    RAG_OPERATOR_RATE_LIMIT_PER_HOUR: 200,
    RAG_INGEST_WORKER_ENABLED: false,
    RAG_OUTBOX_PUBLISH_ENABLED: false,
    INTENT_FILTER_ENABLED: true,
    QUERY_REWRITE_ENABLED: false,
    HYBRID_SEARCH_ENABLED: false,
    RERANK_ENABLED: false,
    SUMMARIZE_ENABLED: false,
    CLOUDINARY_CLOUD_NAME: 'cloud',
    CLOUDINARY_API_KEY: 'cloud-key',
    CLOUDINARY_API_SECRET: 'cloud-secret',
    CLOUDINARY_RAG_FOLDER: 'rag/documents',
  };
}

function makeRuntimeConfigSnapshot(): RuntimeConfigSnapshot {
  return new RuntimeConfigSnapshot(
    new Map(RAG_RUNTIME_CONFIG_DEFINITIONS.map((definition) => [definition.key, definition.defaultValue])),
  );
}

async function signInternalJwt(sub: string, role: string): Promise<string> {
  const token = await new SignJWT({ sub, role, reqId: 'req-rag-phase5' })
    .setProtectedHeader({ alg: 'HS256', typ: 'JWT' })
    .setIssuer(INTERNAL_JWT_ISSUER)
    .setAudience(INTERNAL_JWT_AUDIENCE)
    .setIssuedAt()
    .setExpirationTime('120s')
    .sign(new TextEncoder().encode(INTERNAL_JWT_SECRET));

  return `Bearer ${token}`;
}

function makeConversation(overrides: Partial<RagConversation> = {}): RagConversation {
  return {
    id: CONVERSATION_ID,
    userId: USER_ID,
    operatorId: null,
    role: 'PASSENGER',
    summary: null,
    summaryUpdatedAt: null,
    summaryFromMessageId: null,
    startedAt: new Date('2026-06-13T00:00:00.000Z'),
    lastMessageAt: null,
    createdAt: new Date('2026-06-13T00:00:00.000Z'),
    ...overrides,
  };
}

function makeMessage(role: 'USER' | 'ASSISTANT', content: string): RagMessage {
  return {
    id: role === 'USER' ? USER_MESSAGE_ID : ASSISTANT_MESSAGE_ID,
    conversationId: CONVERSATION_ID,
    role,
    content,
    citedChunkIds: [],
    tokensUsed: null,
    createdAt: new Date('2026-06-13T00:00:00.000Z'),
  };
}

function makeMessageWithConversation(role: 'USER' | 'ASSISTANT', overrides?: Partial<RagConversation>) {
  return {
    ...makeMessage(role, role === 'USER' ? 'Tôi cần hỗ trợ' : 'Xin chào'),
    conversation: makeConversation(overrides),
  };
}

function makeFeedback(): MessageFeedback {
  return {
    id: '88888888-8888-8888-8888-888888888888',
    messageId: ASSISTANT_MESSAGE_ID,
    conversationId: CONVERSATION_ID,
    userId: USER_ID,
    rating: 1,
    queryRewritten: null,
    chunkIds: [CHUNK_ID],
    responseLength: 8,
    createdAt: new Date('2026-06-13T00:00:00.000Z'),
    updatedAt: new Date('2026-06-13T00:00:00.000Z'),
  };
}

function makeChunk() {
  return {
    id: CHUNK_ID,
    documentId: '77777777-7777-7777-7777-777777777777',
    documentTitle: 'FAQ hành khách',
    sectionHeader: 'Hỗ trợ',
    documentType: 'FAQ',
    content: 'Nội dung hỗ trợ hành khách.',
    tokenCount: 10,
    accessLevel: 'PUBLIC',
    operatorId: null,
    distance: 0.1,
  } as const;
}

async function* makeTokenStream(tokens: string[]): AsyncIterable<string> {
  for (const token of tokens) {
    yield token;
  }
}
