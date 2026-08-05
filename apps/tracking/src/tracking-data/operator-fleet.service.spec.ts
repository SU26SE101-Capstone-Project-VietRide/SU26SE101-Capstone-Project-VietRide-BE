import type { RedisService } from '@vietride/nest-redis';
import { OperatorFleetService } from './operator-fleet.service';
import type { OperatorTripProjectionProvider } from './operator-trip-projection.provider';

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
    const service = new OperatorFleetService(redis, provider);

    const result = await service.getLatest('11111111-1111-4111-8111-111111111111', 'IN_PROGRESS');

    expect(mget).toHaveBeenCalledTimes(1);
    expect(mget.mock.calls[0]).toHaveLength(100);
    expect(result.items).toHaveLength(99);
    expect(result.items.every((item) => item.status === 'IN_PROGRESS')).toBe(true);
  });
});
