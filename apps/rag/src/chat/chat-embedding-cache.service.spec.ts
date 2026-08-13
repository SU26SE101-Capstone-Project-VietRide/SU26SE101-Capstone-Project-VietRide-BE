import type { RedisService } from '@vietride/nest-redis';
import type { Env } from '../config/env.schema';
import { ChatEmbeddingCacheService } from './chat-embedding-cache.service';

describe('ChatEmbeddingCacheService', () => {
  const redis = {
    get: jest.fn(),
    set: jest.fn(),
  } as unknown as jest.Mocked<RedisService>;

  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('returns cached embedding vectors', async () => {
    redis.get.mockResolvedValue('[0.1,0.2]');
    const service = new ChatEmbeddingCacheService(redis, makeEnv());

    await expect(service.get('xin chào')).resolves.toEqual([0.1, 0.2]);
  });

  it('ignores invalid cached payloads', async () => {
    redis.get.mockResolvedValue('{"bad":true}');
    const service = new ChatEmbeddingCacheService(redis, makeEnv());

    await expect(service.get('xin chào')).resolves.toBeUndefined();
  });

  it('stores embedding with ttl', async () => {
    const service = new ChatEmbeddingCacheService(redis, makeEnv());

    await service.set('xin chào', [0.1, 0.2]);

    expect(redis.set).toHaveBeenCalledWith(
      expect.stringMatching(
        /^rag:chat:embedding:shopaikey:gemini-embedding-2-preview:2048:/,
      ),
      '[0.1,0.2]',
      3600,
    );
  });

  it('isolates cache keys when the embedding model changes', async () => {
    const service = new ChatEmbeddingCacheService(
      redis,
      makeEnv({ SHOPAIKEY_EMBEDDING_MODEL: 'replacement-embedding-model' }),
    );

    await service.set('xin chào', [0.1, 0.2]);

    expect(redis.set).toHaveBeenCalledWith(
      expect.stringMatching(/^rag:chat:embedding:shopaikey:replacement-embedding-model:2048:/),
      '[0.1,0.2]',
      3600,
    );
  });
});

function makeEnv(overrides: Partial<Env> = {}): Env {
  return {
    SHOPAIKEY_EMBEDDING_MODEL: 'gemini-embedding-2-preview',
    ...overrides,
  } as Env;
}
