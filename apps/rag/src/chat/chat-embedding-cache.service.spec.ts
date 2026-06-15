import type { RedisService } from '@vietride/nest-redis';
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
    const service = new ChatEmbeddingCacheService(redis);

    await expect(service.get('xin chào')).resolves.toEqual([0.1, 0.2]);
  });

  it('ignores invalid cached payloads', async () => {
    redis.get.mockResolvedValue('{"bad":true}');
    const service = new ChatEmbeddingCacheService(redis);

    await expect(service.get('xin chào')).resolves.toBeUndefined();
  });

  it('stores embedding with ttl', async () => {
    const service = new ChatEmbeddingCacheService(redis);

    await service.set('xin chào', [0.1, 0.2]);

    expect(redis.set).toHaveBeenCalledWith(expect.stringMatching(/^rag:chat:embedding:/), '[0.1,0.2]', 3600);
  });
});
