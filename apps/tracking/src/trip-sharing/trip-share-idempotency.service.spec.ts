import { ConflictException, UnprocessableEntityException } from '@nestjs/common';
import type { RedisService } from '@vietride/nest-redis';
import { requireTripShareIdempotencyKey } from './trip-share-idempotency.helpers';
import { TripShareIdempotencyRepository } from './trip-share-idempotency.repository';
import { TripShareIdempotencyService } from './trip-share-idempotency.service';

const KEY = '11111111-1111-4111-8111-111111111111';
const OTHER_KEY = '22222222-2222-4222-8222-222222222222';
const FINGERPRINT = 'a'.repeat(64);
const OTHER_FINGERPRINT = 'b'.repeat(64);

describe('TripShareIdempotencyService', () => {
  it('requires a UUID-v4 Idempotency-Key', () => {
    expect(() => requireTripShareIdempotencyKey(undefined)).toThrow('IDEMPOTENCY_KEY_REQUIRED');
    expect(() => requireTripShareIdempotencyKey('not-a-uuid')).toThrow('VALIDATION_ERROR');
    expect(requireTripShareIdempotencyKey(KEY.toUpperCase())).toBe(KEY);
  });

  it('creates the same length-prefixed fingerprint for canonically equal bodies', () => {
    const first = TripShareIdempotencyService.fingerprint({
      userId: 'user-1', method: 'put', path: '//v1/tracking/trips/1/share-link?ignored=true', tripId: TRIP_ID,
      body: { b: 2, a: 1 },
    });
    const second = TripShareIdempotencyService.fingerprint({
      userId: 'user-1', method: 'PUT', path: '/v1/tracking/trips/1/share-link', tripId: TRIP_ID,
      body: { a: 1, b: 2 },
    });

    expect(first).toBe(second);
    expect(first).toMatch(/^[0-9a-f]{64}$/);
  });

  it('replays a completed outcome with the same fingerprint', async () => {
    const repository = createRepositoryMock();
    repository.readResult.mockResolvedValue({
      fingerprint: FINGERPRINT,
      outcome: { kind: 'SHARE_GRANT', grantId: 'grant-1', expiresAt: '2026-08-04T00:00:00.000Z' },
    });
    const service = new TripShareIdempotencyService(repository);

    await expect(service.begin(KEY, FINGERPRINT)).resolves.toEqual({
      state: 'replay',
      outcome: { kind: 'SHARE_GRANT', grantId: 'grant-1', expiresAt: '2026-08-04T00:00:00.000Z' },
    });
  });

  it('rejects a completed result with a different fingerprint', async () => {
    const repository = createRepositoryMock();
    repository.readResult.mockResolvedValue({
      fingerprint: OTHER_FINGERPRINT,
      outcome: { kind: 'REVOKED', revoked: true },
    });
    const service = new TripShareIdempotencyService(repository);

    await expect(service.begin(KEY, FINGERPRINT)).rejects.toBeInstanceOf(UnprocessableEntityException);
  });

  it('returns pending when the same request owns the active processing key', async () => {
    const repository = createRepositoryMock();
    repository.readResult.mockResolvedValue(null);
    repository.tryAcquire.mockResolvedValue(false);
    repository.readLock.mockResolvedValue(`${FINGERPRINT}:another-owner`);
    const service = new TripShareIdempotencyService(repository);

    await expect(service.begin(KEY, FINGERPRINT)).rejects.toBeInstanceOf(ConflictException);
  });

  it('rejects contention whose active lock has a different fingerprint', async () => {
    const repository = createRepositoryMock();
    repository.readResult.mockResolvedValue(null);
    repository.tryAcquire.mockResolvedValue(false);
    repository.readLock.mockResolvedValue(`${OTHER_FINGERPRINT}:another-owner`);
    const service = new TripShareIdempotencyService(repository);

    await expect(service.begin(KEY, FINGERPRINT)).rejects.toBeInstanceOf(UnprocessableEntityException);
  });

  it('rechecks the result after losing the processing-lock race', async () => {
    const repository = createRepositoryMock();
    repository.readResult
      .mockResolvedValueOnce(null)
      .mockResolvedValueOnce({ fingerprint: FINGERPRINT, outcome: { kind: 'REVOKED', revoked: true } });
    repository.tryAcquire.mockResolvedValue(false);
    const service = new TripShareIdempotencyService(repository);

    await expect(service.begin(KEY, FINGERPRINT)).resolves.toEqual({
      state: 'replay', outcome: { kind: 'REVOKED', revoked: true },
    });
  });

  it('lets a different new key reach the same stable grant through the core boundary', async () => {
    const repository = createRepositoryMock();
    repository.readResult.mockResolvedValue(null);
    repository.tryAcquire.mockResolvedValue(true);
    const service = new TripShareIdempotencyService(repository);
    const coreEnsureActive = jest.fn().mockResolvedValue({ grant: { id: 'stable-grant' }, token: 'stable-token' });

    const first = await service.begin(KEY, FINGERPRINT);
    const second = await service.begin(OTHER_KEY, FINGERPRINT);
    await coreEnsureActive();
    await coreEnsureActive();

    expect(first.state).toBe('acquired');
    expect(second.state).toBe('acquired');
    expect(coreEnsureActive).toHaveBeenNthCalledWith(1);
    expect(coreEnsureActive).toHaveBeenNthCalledWith(2);
    expect(await coreEnsureActive.mock.results[0]?.value).toEqual(await coreEnsureActive.mock.results[1]?.value);
  });
});

describe('TripShareIdempotencyRepository', () => {
  it('uses lowercase operation hashes and exact key namespaces', () => {
    const harness = createRedisHarness();
    const repository = new TripShareIdempotencyRepository(harness.redis as unknown as RedisService);
    const hash = repository.operationHash(KEY);

    expect(hash).toMatch(/^[0-9a-f]{64}$/);
    expect(repository.processingKey(hash)).toBe(`tracking:idem:trip-share:processing:${hash}`);
    expect(repository.resultKey(hash)).toBe(`tracking:idem:trip-share:result:${hash}`);
  });

  it('completes and abandons through owner-safe Lua without persisting token fields', async () => {
    const harness = createRedisHarness();
    const repository = new TripShareIdempotencyRepository(harness.redis as unknown as RedisService);
    const operationHash = repository.operationHash(KEY);
    const lockValue = `${FINGERPRINT}:owner`;
    const unsafeOutcome = {
      kind: 'SHARE_GRANT', grantId: 'grant-1', expiresAt: '2026-08-04T00:00:00.000Z',
      rawToken: 'must-not-persist', shareUrl: 'must-not-persist',
    } as never;

    await expect(repository.complete(operationHash, lockValue, FINGERPRINT, unsafeOutcome)).resolves.toBe(true);
    await expect(repository.abandon(operationHash, lockValue)).resolves.toBe(true);

    const completeCall = harness.client.eval.mock.calls[0] ?? [];
    const abandonCall = harness.client.eval.mock.calls[1] ?? [];
    expect(String(completeCall[0])).toContain("redis.call('GET', KEYS[1])");
    expect(String(abandonCall[0])).toContain("redis.call('GET', KEYS[1])");
    expect(completeCall[1]).toBe(2);
    expect(completeCall[4]).toBe(lockValue);
    expect(completeCall[6]).toBe(86_400);
    expect(String(completeCall[5])).not.toContain('must-not-persist');
  });
});

const TRIP_ID = '33333333-3333-4333-8333-333333333333';

function createRepositoryMock(): jest.Mocked<TripShareIdempotencyRepository> {
  return {
    operationHash: jest.fn((key: string) => key.replaceAll('-', '').padEnd(64, '0').slice(0, 64)),
    processingKey: jest.fn(),
    resultKey: jest.fn(),
    readResult: jest.fn(),
    readLock: jest.fn(),
    tryAcquire: jest.fn(),
    complete: jest.fn(),
    abandon: jest.fn(),
  } as unknown as jest.Mocked<TripShareIdempotencyRepository>;
}

function createRedisHarness() {
  const client = {
    get: jest.fn(),
    set: jest.fn(),
    eval: jest.fn().mockResolvedValue(1),
  };
  return { client, redis: { getClient: () => client } };
}
