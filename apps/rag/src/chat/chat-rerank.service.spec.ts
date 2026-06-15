import { RedisService } from '@vietride/nest-redis';
import { RAG_RUNTIME_CONFIG_DEFINITIONS } from '../config/runtime-config.registry';
import { RuntimeConfigSnapshot } from '../config/runtime-config.service';
import type { ChatCompletionProvider } from '../providers/chat-completion.provider';
import { ChatRerankService } from './chat-rerank.service';
import type { RagRetrievedChunk } from './chat.types';

const CHUNK_A = '11111111-1111-1111-1111-111111111111';
const CHUNK_B = '22222222-2222-2222-2222-222222222222';
const CHUNK_C = '33333333-3333-3333-3333-333333333333';
const CHUNK_D = '44444444-4444-4444-4444-444444444444';
const CHUNK_E = '55555555-5555-5555-5555-555555555555';
const CHUNK_F = '66666666-6666-6666-6666-666666666666';

describe('ChatRerankService', () => {
  let service: ChatRerankService;
  let redis: jest.Mocked<RedisService>;
  let provider: jest.Mocked<ChatCompletionProvider>;

  beforeEach(() => {
    redis = {
      get: jest.fn(),
      set: jest.fn(),
    } as unknown as jest.Mocked<RedisService>;
    provider = {
      complete: jest.fn(),
      stream: jest.fn(),
    };
    service = new ChatRerankService(redis, provider);
    redis.get.mockResolvedValue(null);
  });

  it('orders chunks by provider JSON response and caches the order', async () => {
    provider.complete.mockResolvedValue(JSON.stringify([CHUNK_C, CHUNK_A, CHUNK_B]));

    const result = await service.rerank('hành lý', makeChunks(), makeRuntimeConfigSnapshot());

    expect(result.map((chunk) => chunk.id)).toEqual([CHUNK_C, CHUNK_A, CHUNK_B]);
    expect(redis.set).toHaveBeenCalledWith(expect.stringMatching(/^rag:chat:rerank:/), expect.any(String), 600);
  });

  it('uses cached order without calling provider', async () => {
    redis.get.mockResolvedValue(JSON.stringify([CHUNK_B, CHUNK_A]));

    const result = await service.rerank('hành lý', makeChunks(), makeRuntimeConfigSnapshot());

    expect(provider.complete).not.toHaveBeenCalled();
    expect(result.map((chunk) => chunk.id)).toEqual([CHUNK_B, CHUNK_A]);
  });

  it('falls back to retrieval order when provider response is invalid', async () => {
    provider.complete.mockResolvedValue('not-json');

    const result = await service.rerank('hành lý', makeChunks(), makeRuntimeConfigSnapshot());

    expect(result.map((chunk) => chunk.id)).toEqual([CHUNK_A, CHUNK_B, CHUNK_C, CHUNK_D, CHUNK_E]);
  });
});

function makeRuntimeConfigSnapshot(): RuntimeConfigSnapshot {
  return new RuntimeConfigSnapshot(
    new Map(RAG_RUNTIME_CONFIG_DEFINITIONS.map((definition) => [definition.key, definition.defaultValue])),
  );
}

function makeChunks(): RagRetrievedChunk[] {
  return [CHUNK_A, CHUNK_B, CHUNK_C, CHUNK_D, CHUNK_E, CHUNK_F].map((id, index) => ({
    id,
    documentId: '77777777-7777-7777-7777-777777777777',
    documentTitle: `Document ${index}`,
    sectionHeader: null,
    documentType: 'FAQ',
    content: `Chunk ${index}`,
    tokenCount: 10,
    accessLevel: 'PUBLIC',
    operatorId: null,
    distance: index,
  }));
}
