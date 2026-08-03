import type { RedisService } from '@vietride/nest-redis';
import { TripShareMessageIdempotencyRepository } from './trip-share-message-idempotency.repository';

const MESSAGE_IDENTITY = '11111111-1111-4111-8111-111111111111';

describe('TripShareMessageIdempotencyRepository', () => {
  const get = jest.fn();
  const set = jest.fn();
  const evalScript = jest.fn();
  const redis = {
    getClient: jest.fn(() => ({ get, set, eval: evalScript })),
  } as unknown as RedisService;
  const repository = new TripShareMessageIdempotencyRepository(redis);

  beforeEach(() => jest.clearAllMocks());

  it('checks the exact seven-day processed key without storing a payload', async () => {
    get.mockResolvedValue('1');

    await expect(repository.isProcessed(MESSAGE_IDENTITY)).resolves.toBe(true);

    expect(get).toHaveBeenCalledWith(
      `tracking:trip-share:event:processed:${MESSAGE_IDENTITY}`,
    );
  });

  it('acquires a 120-second owner lock with SET NX', async () => {
    set.mockResolvedValue('OK');

    const ownerToken = await repository.acquire(MESSAGE_IDENTITY);

    expect(ownerToken).toMatch(/^[0-9a-f-]{36}$/);
    expect(set).toHaveBeenCalledWith(
      `tracking:trip-share:event:processing:${MESSAGE_IDENTITY}`,
      ownerToken,
      'EX',
      120,
      'NX',
    );
  });

  it('returns null when another worker owns the processing lock', async () => {
    set.mockResolvedValue(null);

    await expect(repository.acquire(MESSAGE_IDENTITY)).resolves.toBeNull();
  });

  it('atomically marks processed for seven days and deletes only the owned lock', async () => {
    evalScript.mockResolvedValue(1);

    await expect(repository.markProcessed(MESSAGE_IDENTITY, 'owner-token')).resolves.toBe(true);

    expect(evalScript).toHaveBeenCalledWith(
      expect.stringContaining("redis.call('SET', KEYS[2], '1', 'EX', ARGV[2])"),
      2,
      `tracking:trip-share:event:processing:${MESSAGE_IDENTITY}`,
      `tracking:trip-share:event:processed:${MESSAGE_IDENTITY}`,
      'owner-token',
      604_800,
    );
  });

  it('releases only the lock owned by the supplied token', async () => {
    evalScript.mockResolvedValue(1);

    await expect(repository.release(MESSAGE_IDENTITY, 'owner-token')).resolves.toBe(true);

    expect(evalScript).toHaveBeenCalledWith(
      expect.stringContaining("redis.call('GET', KEYS[1])"),
      1,
      `tracking:trip-share:event:processing:${MESSAGE_IDENTITY}`,
      'owner-token',
    );
  });

  it('never passes an event payload or share token to Redis', async () => {
    set.mockResolvedValue('OK');
    evalScript.mockResolvedValue(1);
    const ownerToken = await repository.acquire(MESSAGE_IDENTITY);
    await repository.markProcessed(MESSAGE_IDENTITY, ownerToken as string);

    const redisArguments = JSON.stringify([
      ...get.mock.calls,
      ...set.mock.calls,
      ...evalScript.mock.calls.map((call) => call.slice(1)),
    ]);
    expect(redisArguments).not.toContain('raw-event-payload');
    expect(redisArguments).not.toContain('share-token');
  });
});
