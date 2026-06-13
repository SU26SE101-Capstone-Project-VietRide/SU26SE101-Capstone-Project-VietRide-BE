import { ForbiddenException, ServiceUnavailableException } from '@nestjs/common';
import type { Env } from '../config/env.schema';
import type { RagConversation, RagMessage } from '../generated/rag-prisma-client';
import type { ChatCompletionProvider } from '../providers/chat-completion.provider';
import type { EmbeddingProvider } from '../providers/embedding.provider';
import { ChatEmbeddingCacheService } from './chat-embedding-cache.service';
import { ChatRateLimitService } from './chat-rate-limit.service';
import { ChatRepository } from './chat.repository';
import { ChatService } from './chat.service';

const USER_ID = '11111111-1111-1111-1111-111111111111';
const OPERATOR_ID = '22222222-2222-2222-2222-222222222222';
const CONVERSATION_ID = '33333333-3333-3333-3333-333333333333';
const USER_MESSAGE_ID = '44444444-4444-4444-4444-444444444444';
const ASSISTANT_MESSAGE_ID = '55555555-5555-5555-5555-555555555555';
const CHUNK_ID = '66666666-6666-6666-6666-666666666666';

describe('ChatService', () => {
  let service: ChatService;
  let repository: jest.Mocked<ChatRepository>;
  let embeddingCache: jest.Mocked<ChatEmbeddingCacheService>;
  let rateLimit: jest.Mocked<ChatRateLimitService>;
  let chatProvider: jest.Mocked<ChatCompletionProvider>;
  let embeddingProvider: jest.Mocked<EmbeddingProvider>;

  beforeEach(() => {
    repository = {
      findConversation: jest.fn(),
      createConversation: jest.fn(),
      createUserMessage: jest.fn(),
      createAssistantMessage: jest.fn(),
      findRecentMessages: jest.fn(),
      searchChunks: jest.fn(),
    } as unknown as jest.Mocked<ChatRepository>;
    embeddingCache = {
      get: jest.fn(),
      set: jest.fn(),
    } as unknown as jest.Mocked<ChatEmbeddingCacheService>;
    rateLimit = {
      assertAllowed: jest.fn(),
    } as unknown as jest.Mocked<ChatRateLimitService>;
    chatProvider = {
      complete: jest.fn(),
      stream: jest.fn(),
    };
    embeddingProvider = {
      embed: jest.fn(),
    };
    service = new ChatService(repository, embeddingCache, rateLimit, chatProvider, embeddingProvider, makeEnv());

    repository.createConversation.mockResolvedValue(makeConversation());
    repository.createUserMessage.mockResolvedValue(makeMessage('USER', 'Tôi cần hỗ trợ'));
    repository.createAssistantMessage.mockResolvedValue(makeMessage('ASSISTANT', 'Câu trả lời'));
    repository.findRecentMessages.mockResolvedValue([]);
    repository.searchChunks.mockResolvedValue([makeChunk()]);
    embeddingCache.get.mockResolvedValue(undefined);
    embeddingProvider.embed.mockResolvedValue([0.1, 0.2]);
    chatProvider.stream.mockReturnValue(makeTokenStream(['Xin ', 'chào']));
  });

  it('uses PUBLIC-only retrieval for passenger callers', async () => {
    await service.prepareChat({ message: 'Tôi cần hỗ trợ' }, { sub: USER_ID, role: 'PASSENGER' });

    expect(repository.searchChunks).toHaveBeenCalledWith(
      expect.objectContaining({
        accessLevels: ['PUBLIC'],
        limit: 5,
      }),
    );
  });

  it('checks rate limit before retrieval', async () => {
    await service.prepareChat({ message: 'Tôi cần hỗ trợ' }, { sub: USER_ID, role: 'PASSENGER' });

    expect(rateLimit.assertAllowed).toHaveBeenCalledWith(
      expect.objectContaining({ sub: USER_ID, role: 'PASSENGER' }),
    );
  });

  it('uses cached query embedding when available', async () => {
    embeddingCache.get.mockResolvedValue([0.9, 0.8]);

    await service.prepareChat({ message: 'Tôi cần hỗ trợ' }, { sub: USER_ID, role: 'PASSENGER' });

    expect(embeddingProvider.embed).not.toHaveBeenCalled();
    expect(embeddingCache.set).not.toHaveBeenCalled();
    expect(repository.searchChunks).toHaveBeenCalledWith(
      expect.objectContaining({ queryEmbedding: [0.9, 0.8] }),
    );
  });

  it('stores query embedding when cache misses', async () => {
    await service.prepareChat({ message: 'Tôi cần hỗ trợ' }, { sub: USER_ID, role: 'PASSENGER' });

    expect(embeddingCache.set).toHaveBeenCalledWith('Tôi cần hỗ trợ', [0.1, 0.2]);
  });

  it('uses PUBLIC and OPERATOR retrieval with tenant filter for operator-scoped callers', async () => {
    await service.prepareChat(
      { message: 'Quy trình vận hành' },
      { sub: USER_ID, role: 'OPERATOR_ADMIN', operatorId: OPERATOR_ID },
    );

    expect(repository.searchChunks).toHaveBeenCalledWith(
      expect.objectContaining({
        accessLevels: ['PUBLIC', 'OPERATOR'],
        operatorId: OPERATOR_ID,
      }),
    );
  });

  it('rejects operator-scoped roles without operatorId', async () => {
    await expect(
      service.prepareChat({ message: 'Quy trình vận hành' }, { sub: USER_ID, role: 'OPERATOR_ADMIN' }),
    ).rejects.toBeInstanceOf(ForbiddenException);
  });

  it('persists assistant message with retrieved citations after streaming', async () => {
    const prepared = await service.prepareChat(
      { message: 'Tôi cần hỗ trợ' },
      { sub: USER_ID, role: 'PASSENGER' },
    );

    const events = [];
    for await (const event of service.streamPrepared(prepared)) {
      events.push(event);
    }

    expect(repository.createAssistantMessage).toHaveBeenCalledWith(
      CONVERSATION_ID,
      'Xin chào',
      [CHUNK_ID],
    );
    expect(events).toContainEqual(
      expect.objectContaining({
        event: 'done',
        data: expect.objectContaining({ assistantMessageId: ASSISTANT_MESSAGE_ID }),
      }),
    );
  });

  it('emits an SSE error event when provider stream fails', async () => {
    chatProvider.stream.mockReturnValue(makeFailingStream());
    const prepared = await service.prepareChat(
      { message: 'Tôi cần hỗ trợ' },
      { sub: USER_ID, role: 'PASSENGER' },
    );

    const events = [];
    for await (const event of service.streamPrepared(prepared)) {
      events.push(event);
    }

    expect(events).toContainEqual(
      expect.objectContaining({
        event: 'error',
        data: expect.objectContaining({ code: 'RAG_PROVIDER_UNAVAILABLE' }),
      }),
    );
  });
});

function makeEnv(): Env {
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
    INTERNAL_JWT_SECRET: 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa',
    INTERNAL_JWT_TTL_SEC: 120,
    JWT_ISSUER: 'vietride-identity',
    JWT_AUDIENCE: 'vietride-api',
    LOG_LEVEL: 'info',
    OPENROUTER_API_KEY: 'test-key',
    OPENROUTER_BASE_URL: 'https://openrouter.ai/api/v1',
    OPENROUTER_CHAT_MODEL: 'nex-agi/nex-n2-pro:free',
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
    INTENT_FILTER_ENABLED: false,
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

function makeConversation(): RagConversation {
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

function makeFailingStream(): AsyncIterable<string> {
  return {
    [Symbol.asyncIterator]() {
      return {
        async next(): Promise<IteratorResult<string>> {
          throw new ServiceUnavailableException({
            errorCode: 'RAG_PROVIDER_UNAVAILABLE',
            detail: 'Provider unavailable',
          });
        },
      };
    },
  };
}
