import { ServiceUnavailableException } from '@nestjs/common';
import type { RedisService } from '@vietride/nest-redis';
import type { Env } from '../config/env.schema';
import { TripShareRateLimiter } from './trip-share-rate-limiter';

const RAW_TOKEN = 'v1.11111111-1111-4111-8111-111111111111.sensitive-signature';
const NOW_MS = Date.parse('2026-08-03T00:00:30.000Z');

describe('TripShareRateLimiter', () => {
  const env = {
    TRACKING_SHARE_CONTEXT_RATE_LIMIT_PER_MIN: 2,
    TRACKING_SHARE_SOCKET_RATE_LIMIT_PER_MIN: 1,
  } as Env;

  it('allows requests below the configured limit and uses atomic Lua with TTL', async () => {
    const harness = createRedisHarness(1);
    const limiter = new TripShareRateLimiter(harness.redis as unknown as RedisService, env);

    await expect(limiter.consume('context', RAW_TOKEN, NOW_MS)).resolves.toBeUndefined();

    const call = harness.client.eval.mock.calls[0] ?? [];
    expect(String(call[0])).toContain("redis.call('INCR', KEYS[1])");
    expect(call[1]).toBe(1);
    expect(call[3]).toBe(60);
    expect(String(call[2])).toMatch(/^tracking:share:rate:context:[0-9a-f]{64}:\d+$/);
    expect(String(call[2])).not.toContain(RAW_TOKEN);
  });

  it('throws 429 above the configured surface limit', async () => {
    const harness = createRedisHarness(3);
    const limiter = new TripShareRateLimiter(harness.redis as unknown as RedisService, env);

    await expect(limiter.consume('context', RAW_TOKEN, NOW_MS)).rejects.toMatchObject({
      status: 429,
    });
  });

  it('isolates context and socket surfaces', async () => {
    const harness = createRedisHarness(1);
    const limiter = new TripShareRateLimiter(harness.redis as unknown as RedisService, env);

    await limiter.consume('context', RAW_TOKEN, NOW_MS);
    await limiter.consume('socket', RAW_TOKEN, NOW_MS);

    expect(harness.client.eval.mock.calls[0]?.[2]).toContain(':context:');
    expect(harness.client.eval.mock.calls[1]?.[2]).toContain(':socket:');
  });

  it('fails closed with 503 when Redis fails', async () => {
    const harness = createRedisHarness(new Error('redis unavailable'));
    const limiter = new TripShareRateLimiter(harness.redis as unknown as RedisService, env);

    await expect(limiter.consume('context', RAW_TOKEN, NOW_MS))
      .rejects.toBeInstanceOf(ServiceUnavailableException);
  });
});

function createRedisHarness(result: number | Error) {
  const client = {
    eval: result instanceof Error ? jest.fn().mockRejectedValue(result) : jest.fn().mockResolvedValue(result),
  };
  return { client, redis: { getClient: () => client } };
}
