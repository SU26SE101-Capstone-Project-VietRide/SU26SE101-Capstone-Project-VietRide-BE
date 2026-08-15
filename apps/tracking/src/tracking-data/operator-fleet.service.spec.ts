import type { RedisService } from '@vietride/nest-redis';
import { OperatorFleetService } from './operator-fleet.service';
import type { OperatorTripProjectionProvider } from './operator-trip-projection.provider';
import type { OperatorShuttleProjectionProvider } from './operator-shuttle-projection.provider';

describe('OperatorFleetService', () => {
  it('loads 100 latest GPS values with one Redis MGET and omits missing signals', async () => {
    const projections = Array.from({ length: 100 }, (_, index) => ({
      tripId: `00000000-0000-4000-8000-${index.toString().padStart(12, '0')}`,
      status: 'IN_PROGRESS',
    }));
    const mget = jest.fn(async (...keys: string[]) => keys.map((_, index) => {
      const projection = projections[index];
      if (index === 99 || !projection) return null;
      return JSON.stringify({
        tripId: projection.tripId,
        latitude: 10 + index / 1000,
        longitude: 106 + index / 1000,
        recordedAt: '2026-08-05T03:00:00.000Z',
      });
    }));
    const redis = { getClient: () => ({ mget }) } as unknown as RedisService;
    const provider = { list: jest.fn(async () => projections) } as unknown as OperatorTripProjectionProvider;
    const shuttleProvider = { list: jest.fn() } as unknown as OperatorShuttleProjectionProvider;
    const service = new OperatorFleetService(redis, provider, shuttleProvider);

    const result = await service.getLatest('11111111-1111-4111-8111-111111111111', 'IN_PROGRESS');

    expect(mget).toHaveBeenCalledTimes(1);
    expect(mget.mock.calls[0]).toHaveLength(100);
    expect(result.items).toHaveLength(99);
    expect(result.items.every((item) => item.kind === 'TRIP')).toBe(true);
    expect(result.items.every((item) => item.status === 'IN_PROGRESS')).toBe(true);
    expect(shuttleProvider.list).not.toHaveBeenCalled();
  });

  it('merges active Shuttle GPS with one Redis MGET and maps heading to headingDeg', async () => {
    const tripId = '11111111-1111-4111-8111-111111111111';
    const shuttleTripId = '22222222-2222-4222-8222-222222222222';
    const mainTripId = '33333333-3333-4333-8333-333333333333';
    const mget = jest.fn(async (...keys: string[]) => keys.map((key) => key.includes(':shuttle:')
      ? JSON.stringify({
          shuttleTripId,
          latitude: 10.7,
          longitude: 106.7,
          speedKmh: 24,
          heading: 120,
          recordedAt: '2026-08-15T03:00:00.000Z',
        })
      : JSON.stringify({
          tripId,
          latitude: 10.5,
          longitude: 106.5,
          recordedAt: '2026-08-15T03:00:00.000Z',
        })));
    const redis = { getClient: () => ({ mget }) } as unknown as RedisService;
    const tripProvider = {
      list: jest.fn(async () => [{ tripId, status: 'IN_PROGRESS' }]),
    } as unknown as OperatorTripProjectionProvider;
    const shuttleProvider = {
      list: jest.fn(async () => [{ shuttleTripId, mainTripId, status: 'IN_PROGRESS' }]),
    } as unknown as OperatorShuttleProjectionProvider;
    const service = new OperatorFleetService(redis, tripProvider, shuttleProvider);

    const result = await service.getLatest(
      '44444444-4444-4444-8444-444444444444',
      'IN_PROGRESS',
      true,
    );

    expect(mget).toHaveBeenCalledTimes(1);
    expect(mget.mock.calls[0]).toEqual([
      `tracking:latest:${tripId}`,
      `tracking:shuttle:latest:${shuttleTripId}`,
    ]);
    expect(result.items).toEqual([
      expect.objectContaining({ kind: 'TRIP', tripId, status: 'IN_PROGRESS' }),
      expect.objectContaining({
        kind: 'SHUTTLE',
        shuttleTripId,
        mainTripId,
        status: 'IN_PROGRESS',
        headingDeg: 120,
      }),
    ]);
  });

  it('does not request Shuttle projections for a non-IN_PROGRESS status filter', async () => {
    const redis = { getClient: () => ({ mget: jest.fn(async () => []) }) } as unknown as RedisService;
    const tripProvider = { list: jest.fn(async () => []) } as unknown as OperatorTripProjectionProvider;
    const shuttleProvider = { list: jest.fn(async () => []) } as unknown as OperatorShuttleProjectionProvider;
    const service = new OperatorFleetService(redis, tripProvider, shuttleProvider);

    await service.getLatest(
      '44444444-4444-4444-8444-444444444444',
      'COMPLETED',
      true,
    );

    expect(shuttleProvider.list).not.toHaveBeenCalled();
  });

  it('maps projection failures to a stable 503 fleet error', async () => {
    const redis = { getClient: jest.fn() } as unknown as RedisService;
    const tripProvider = {
      list: jest.fn(async () => { throw new Error('TRIP_UNAVAILABLE'); }),
    } as unknown as OperatorTripProjectionProvider;
    const shuttleProvider = { list: jest.fn() } as unknown as OperatorShuttleProjectionProvider;
    const service = new OperatorFleetService(redis, tripProvider, shuttleProvider);

    await expect(service.getLatest(
      '44444444-4444-4444-8444-444444444444',
    )).rejects.toMatchObject({
      status: 503,
      response: { errorCode: 'TRACKING_FLEET_UNAVAILABLE' },
    });
  });
});
