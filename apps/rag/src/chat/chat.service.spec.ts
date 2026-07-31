import { ForbiddenException, ServiceUnavailableException } from '@nestjs/common';
import type { Env } from '../config/env.schema';
import { RAG_RUNTIME_CONFIG_DEFINITIONS } from '../config/runtime-config.registry';
import { RuntimeConfigService, RuntimeConfigSnapshot } from '../config/runtime-config.service';
import type { RagConversation, RagMessage } from '../generated/rag-prisma-client';
import type { ChatCompletionProvider } from '../providers/chat-completion.provider';
import type { EmbeddingProvider } from '../providers/embedding.provider';
import { ChatEmbeddingCacheService } from './chat-embedding-cache.service';
import { ChatIntentService } from './chat-intent.service';
import { ChatQueryRewriteService } from './chat-query-rewrite.service';
import { ChatRateLimitService } from './chat-rate-limit.service';
import { ChatRepository } from './chat.repository';
import { ChatRerankService } from './chat-rerank.service';
import { ChatService } from './chat.service';
import { ChatSummaryService } from './chat-summary.service';
import type { RagRetrievedChunk } from './chat.types';

const USER_ID = '11111111-1111-1111-1111-111111111111';
const OPERATOR_ID = '22222222-2222-2222-2222-222222222222';
const CONVERSATION_ID = '33333333-3333-3333-3333-333333333333';
const USER_MESSAGE_ID = '44444444-4444-4444-4444-444444444444';
const ASSISTANT_MESSAGE_ID = '55555555-5555-5555-5555-555555555555';
const CHUNK_ID = '66666666-6666-6666-6666-666666666666';
const SECOND_CHUNK_ID = '99999999-9999-9999-9999-999999999999';

describe('ChatService', () => {
  let service: ChatService;
  let repository: jest.Mocked<ChatRepository>;
  let embeddingCache: jest.Mocked<ChatEmbeddingCacheService>;
  let rateLimit: jest.Mocked<ChatRateLimitService>;
  let intentService: jest.Mocked<ChatIntentService>;
  let queryRewriteService: jest.Mocked<ChatQueryRewriteService>;
  let summaryService: jest.Mocked<ChatSummaryService>;
  let rerankService: jest.Mocked<ChatRerankService>;
  let chatProvider: jest.Mocked<ChatCompletionProvider>;
  let embeddingProvider: jest.Mocked<EmbeddingProvider>;
  let runtimeConfig: jest.Mocked<RuntimeConfigService>;
  let runtimeConfigSnapshot: RuntimeConfigSnapshot;

  beforeEach(() => {
    repository = {
      findConversation: jest.fn(),
      createConversation: jest.fn(),
      createUserMessage: jest.fn(),
      createAssistantMessage: jest.fn(),
      findRecentMessages: jest.fn(),
      countMessages: jest.fn(),
      updateConversationSummary: jest.fn(),
      searchChunks: jest.fn(),
    } as unknown as jest.Mocked<ChatRepository>;
    embeddingCache = {
      get: jest.fn(),
      set: jest.fn(),
    } as unknown as jest.Mocked<ChatEmbeddingCacheService>;
    rateLimit = {
      assertAllowed: jest.fn(),
    } as unknown as jest.Mocked<ChatRateLimitService>;
    intentService = {
      classify: jest.fn(),
    } as unknown as jest.Mocked<ChatIntentService>;
    queryRewriteService = {
      rewriteIfNeeded: jest.fn(),
    } as unknown as jest.Mocked<ChatQueryRewriteService>;
    summaryService = {
      summarizeIfNeeded: jest.fn(),
    } as unknown as jest.Mocked<ChatSummaryService>;
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
    runtimeConfigSnapshot = makeRuntimeConfigSnapshot();
    runtimeConfig = {
      getSnapshot: jest.fn().mockResolvedValue(runtimeConfigSnapshot),
    } as unknown as jest.Mocked<RuntimeConfigService>;
    service = new ChatService(
      repository,
      embeddingCache,
      rateLimit,
      intentService,
      queryRewriteService,
      summaryService,
      rerankService,
      chatProvider,
      embeddingProvider,
      makeEnv(),
      runtimeConfig,
    );

    repository.createConversation.mockResolvedValue(makeConversation());
    repository.createUserMessage.mockResolvedValue(makeMessage('USER', 'Tôi cần hỗ trợ'));
    repository.createAssistantMessage.mockResolvedValue(makeMessage('ASSISTANT', 'Câu trả lời'));
    repository.findRecentMessages.mockResolvedValue([]);
    repository.searchChunks.mockResolvedValue([makeChunk()]);
    intentService.classify.mockResolvedValue({ allowed: true });
    queryRewriteService.rewriteIfNeeded.mockImplementation(async (message) => message);
    rerankService.rerank.mockImplementation(async (_query, chunks) => chunks.slice(0, 5));
    embeddingCache.get.mockResolvedValue(undefined);
    embeddingProvider.embed.mockResolvedValue([0.1, 0.2]);
    chatProvider.stream.mockReturnValue(makeTokenStream(['Xin ', 'chào']));
  });

  it('uses PUBLIC-only retrieval for passenger callers', async () => {
    await service.prepareChat({ message: 'Tôi cần hỗ trợ' }, { sub: USER_ID, role: 'PASSENGER' });

    expect(repository.searchChunks).toHaveBeenCalledWith(
      expect.objectContaining({
        queryText: 'Tôi cần hỗ trợ',
        accessLevels: ['PUBLIC'],
        limit: 5,
        hybridSearchEnabled: false,
      }),
    );
  });

  it('passes hybrid search flag to repository when enabled', async () => {
    service = new ChatService(
      repository,
      embeddingCache,
      rateLimit,
      intentService,
      queryRewriteService,
      summaryService,
      rerankService,
      chatProvider,
      embeddingProvider,
      makeEnv({ HYBRID_SEARCH_ENABLED: true }),
      runtimeConfig,
    );

    await service.prepareChat({ message: 'Tôi cần hỗ trợ' }, { sub: USER_ID, role: 'PASSENGER' });

    expect(repository.searchChunks).toHaveBeenCalledWith(
      expect.objectContaining({
        queryText: 'Tôi cần hỗ trợ',
        hybridSearchEnabled: true,
      }),
    );
  });

  it('refuses off-topic messages without retrieval when intent filter is enabled', async () => {
    service = new ChatService(
      repository,
      embeddingCache,
      rateLimit,
      intentService,
      queryRewriteService,
      summaryService,
      rerankService,
      chatProvider,
      embeddingProvider,
      makeEnv({ INTENT_FILTER_ENABLED: true }),
      runtimeConfig,
    );
    intentService.classify.mockResolvedValue({
      allowed: false,
      refusalMessage: 'Tôi chỉ hỗ trợ VietRide.',
    });

    const prepared = await service.prepareChat(
      { message: 'Viết thơ về biển' },
      { sub: USER_ID, role: 'PASSENGER' },
    );
    const events = [];
    for await (const event of service.streamPrepared(prepared)) {
      events.push(event);
    }

    expect(embeddingProvider.embed).not.toHaveBeenCalled();
    expect(repository.searchChunks).not.toHaveBeenCalled();
    expect(repository.createAssistantMessage).toHaveBeenCalledWith(
      CONVERSATION_ID,
      'Tôi chỉ hỗ trợ VietRide.',
      [],
    );
    expect(events).toContainEqual(
      expect.objectContaining({
        event: 'done',
        data: expect.objectContaining({ citedChunkIds: [] }),
      }),
    );
  });

  it('uses rewritten query for retrieval when query rewrite is enabled', async () => {
    service = new ChatService(
      repository,
      embeddingCache,
      rateLimit,
      intentService,
      queryRewriteService,
      summaryService,
      rerankService,
      chatProvider,
      embeddingProvider,
      makeEnv({ QUERY_REWRITE_ENABLED: true }),
      runtimeConfig,
    );
    repository.findRecentMessages.mockResolvedValue([
      makeMessage('ASSISTANT', 'Hoàn tiền mất 3 ngày.'),
    ]);
    queryRewriteService.rewriteIfNeeded.mockResolvedValue(
      'Thời gian hoàn tiền VietRide là bao lâu?',
    );

    await service.prepareChat({ message: 'Vậy mất bao lâu?' }, { sub: USER_ID, role: 'PASSENGER' });

    expect(embeddingCache.get).toHaveBeenCalledWith('Thời gian hoàn tiền VietRide là bao lâu?');
    expect(repository.searchChunks).toHaveBeenCalledWith(
      expect.objectContaining({
        queryText: 'Thời gian hoàn tiền VietRide là bao lâu?',
      }),
    );
  });

  it('summarizes after the assistant message when summarization is enabled', async () => {
    service = new ChatService(
      repository,
      embeddingCache,
      rateLimit,
      intentService,
      queryRewriteService,
      summaryService,
      rerankService,
      chatProvider,
      embeddingProvider,
      makeEnv({ SUMMARIZE_ENABLED: true }),
      runtimeConfig,
    );

    const prepared = await service.prepareChat(
      { message: 'Tôi cần hỗ trợ' },
      { sub: USER_ID, role: 'PASSENGER' },
    );
    for await (const unused of service.streamPrepared(prepared)) {
      void unused;
    }

    expect(summaryService.summarizeIfNeeded).toHaveBeenCalledWith(
      expect.objectContaining({ id: CONVERSATION_ID }),
      ASSISTANT_MESSAGE_ID,
      runtimeConfigSnapshot,
    );
  });

  it('retrieves 10 candidates and reranks when rerank is enabled', async () => {
    service = new ChatService(
      repository,
      embeddingCache,
      rateLimit,
      intentService,
      queryRewriteService,
      summaryService,
      rerankService,
      chatProvider,
      embeddingProvider,
      makeEnv({ RERANK_ENABLED: true }),
      runtimeConfig,
    );

    await service.prepareChat({ message: 'Tôi cần hỗ trợ' }, { sub: USER_ID, role: 'PASSENGER' });

    expect(repository.searchChunks).toHaveBeenCalledWith(
      expect.objectContaining({
        limit: 10,
      }),
    );
    expect(rerankService.rerank).toHaveBeenCalledWith(
      'Tôi cần hỗ trợ',
      [makeChunk()],
      runtimeConfigSnapshot,
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
      service.prepareChat(
        { message: 'Quy trình vận hành' },
        { sub: USER_ID, role: 'OPERATOR_ADMIN' },
      ),
    ).rejects.toBeInstanceOf(ForbiddenException);
  });

  it('rejects non-admin sending operatorId in DTO', async () => {
    await expect(
      service.prepareChat(
        { message: 'Quy trình', operatorId: OPERATOR_ID },
        { sub: USER_ID, role: 'PASSENGER' },
      ),
    ).rejects.toBeInstanceOf(ForbiddenException);
  });

  it('uses DTO operatorId for SYSTEM_ADMIN retrieval scope', async () => {
    repository.createConversation.mockResolvedValue(makeConversation({ operatorId: OPERATOR_ID }));

    await service.prepareChat(
      { message: 'Admin query', operatorId: OPERATOR_ID },
      { sub: USER_ID, role: 'SYSTEM_ADMIN' },
    );

    expect(repository.createConversation).toHaveBeenCalledWith(
      expect.objectContaining({ operatorId: OPERATOR_ID }),
    );
    expect(repository.searchChunks).toHaveBeenCalledWith(
      expect.objectContaining({
        operatorId: OPERATOR_ID,
        callerRole: 'SYSTEM_ADMIN',
      }),
    );
  });

  it('uses global scope for SYSTEM_ADMIN without operatorId', async () => {
    await service.prepareChat(
      { message: 'Admin global query' },
      { sub: USER_ID, role: 'SYSTEM_ADMIN' },
    );

    expect(repository.searchChunks).toHaveBeenCalledWith(
      expect.objectContaining({
        accessLevels: ['PUBLIC', 'OPERATOR', 'ADMIN'],
        callerRole: 'SYSTEM_ADMIN',
      }),
    );
    expect(repository.searchChunks).toHaveBeenCalledWith(
      expect.not.objectContaining({ operatorId: expect.anything() }),
    );
  });

  it('passes callerRole for audience filtering in search', async () => {
    await service.prepareChat({ message: 'Test audience' }, { sub: USER_ID, role: 'PASSENGER' });

    expect(repository.searchChunks).toHaveBeenCalledWith(
      expect.objectContaining({ callerRole: 'PASSENGER' }),
    );
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

    expect(repository.createAssistantMessage).toHaveBeenCalledWith(CONVERSATION_ID, 'Xin chào', [
      CHUNK_ID,
    ]);
    expect(events).toContainEqual(
      expect.objectContaining({
        event: 'done',
        data: expect.objectContaining({ assistantMessageId: ASSISTANT_MESSAGE_ID }),
      }),
    );
  });

  it('cites only chunks included in the provider context budget', async () => {
    service = new ChatService(
      repository,
      embeddingCache,
      rateLimit,
      intentService,
      queryRewriteService,
      summaryService,
      rerankService,
      chatProvider,
      embeddingProvider,
      makeEnv({ RAG_MAX_CONTEXT_TOKENS: 15 }),
      runtimeConfig,
    );
    repository.searchChunks.mockResolvedValue([
      makeChunk({ id: CHUNK_ID, tokenCount: 10 }),
      makeChunk({ id: SECOND_CHUNK_ID, tokenCount: 10 }),
    ]);

    const prepared = await service.prepareChat(
      { message: 'Tôi cần hỗ trợ' },
      { sub: USER_ID, role: 'PASSENGER' },
    );
    for await (const unused of service.streamPrepared(prepared)) {
      void unused;
    }

    expect(prepared.chunks.map((chunk) => chunk.id)).toEqual([CHUNK_ID]);
    expect(repository.createAssistantMessage).toHaveBeenCalledWith(CONVERSATION_ID, 'Xin chào', [
      CHUNK_ID,
    ]);
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

function makeEnv(overrides: Partial<Env> = {}): Env {
  return {
    NODE_ENV: 'test',
    PORT: 3003,
    GATEWAY_URL: 'http://gateway:3000',
    IDENTITY_INTERNAL_BASE_URL: 'http://identity:5001',
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
    INTENT_FILTER_ENABLED: false,
    QUERY_REWRITE_ENABLED: false,
    HYBRID_SEARCH_ENABLED: false,
    RERANK_ENABLED: false,
    SUMMARIZE_ENABLED: false,
    CLOUDINARY_CLOUD_NAME: 'cloud',
    CLOUDINARY_API_KEY: 'cloud-key',
    CLOUDINARY_API_SECRET: 'cloud-secret',
    CLOUDINARY_RAG_FOLDER: 'rag/documents',
    ...overrides,
  };
}

function makeRuntimeConfigSnapshot(): RuntimeConfigSnapshot {
  return new RuntimeConfigSnapshot(
    new Map(
      RAG_RUNTIME_CONFIG_DEFINITIONS.map((definition) => [definition.key, definition.defaultValue]),
    ),
  );
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

function makeChunk(overrides: Partial<RagRetrievedChunk> = {}): RagRetrievedChunk {
  return { ...makeChunkBase(), ...overrides };
}

function makeChunkBase(): RagRetrievedChunk {
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
  };
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
