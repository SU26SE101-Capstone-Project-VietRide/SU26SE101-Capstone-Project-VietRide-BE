import type { Env } from '../config/env.schema';
import type { RedisService } from '@vietride/nest-redis';
import { TripShareSubstitutionStateRepository } from './trip-share-substitution-state.repository';

describe('TripShareSubstitutionStateRepository', () => {
  const values = new Map<string, string>();
  const redis = {
    get: jest.fn(async (key: string) => values.get(key) ?? null),
    set: jest.fn(async (key: string, value: string) => {
      values.set(key, value);
      return 'OK';
    }),
    del: jest.fn(async (key: string) => values.delete(key) ? 1 : 0),
  };
  const repository = new TripShareSubstitutionStateRepository(
    { getClient: () => redis } as unknown as RedisService,
    { TRACKING_SHARE_TOKEN_TTL_SECONDS: 86_400 } as Env,
  );

  beforeEach(() => {
    values.clear();
    jest.clearAllMocks();
  });

  it('stores bidirectional aliases with the share-token TTL', async () => {
    await repository.storeAlias('old-trip', 'new-trip');

    expect(redis.set).toHaveBeenCalledWith(
      'tracking:trip-share:substitution:next:old-trip',
      'new-trip',
      'EX',
      86_400,
    );
    expect(redis.set).toHaveBeenCalledWith(
      'tracking:trip-share:substitution:previous:new-trip',
      'old-trip',
      'EX',
      86_400,
    );
  });

  it('resolves a bounded multi-substitution chain', async () => {
    await repository.storeAlias('trip-a', 'trip-b');
    await repository.storeAlias('trip-b', 'trip-c');

    await expect(repository.resolveCurrentTripId('trip-a')).resolves.toBe('trip-c');
    await expect(repository.findPrevious('trip-c')).resolves.toBe('trip-b');
    await expect(repository.listPreviousTripIds('trip-c')).resolves.toEqual([
      'trip-b',
      'trip-a',
    ]);
  });

  it('rejects an alias cycle', async () => {
    await repository.storeAlias('trip-a', 'trip-b');
    await repository.storeAlias('trip-b', 'trip-a');

    await expect(repository.resolveCurrentTripId('trip-a')).rejects.toThrow(
      'TRIP_SHARE_SUBSTITUTION_ALIAS_CYCLE',
    );
  });

  it('marks and clears the pending state without storing a share token', async () => {
    await repository.markPending('trip-a', '2026-08-31T08:00:00.000Z');
    await expect(repository.isPending('trip-a')).resolves.toBe(true);
    await repository.clearPending('trip-a');
    await expect(repository.isPending('trip-a')).resolves.toBe(false);
    expect(JSON.stringify([...values])).not.toContain('v1.');
  });
});
