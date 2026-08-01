import { RedisService } from '@vietride/nest-redis';
import type { Env } from '../config/env.schema';
import type { TrackingInternalJwtSigner } from '../authorization/tracking-internal-jwt.signer';
import { ShuttleService } from './shuttle.service';

describe('ShuttleService', () => {
  it('writes only shuttle Redis keys without calculating ETA on the live GPS path', async () => {
    const client = {
      eval: jest.fn(async () => 1),
    };
    const redis = { getClient: jest.fn(() => client) } as unknown as RedisService;
    const signer = { sign: jest.fn() } as unknown as TrackingInternalJwtSigner;
    const service = new ShuttleService(redis, signer, {} as Env);

    const result = await service.recordLocation(
      {
        shuttleTripId: '36000000-0000-4000-8000-000000000001',
        latitude: 10.77,
        longitude: 106.7,
        speedKmh: 30,
        recordedAt: '2026-07-13T01:00:00.000Z',
      },
    );

    expect(result.gps.shuttleTripId).toBe('36000000-0000-4000-8000-000000000001');
    expect(result.duplicate).toBe(false);
    expect(client.eval).toHaveBeenCalledWith(
      expect.any(String),
      3,
      'tracking:shuttle:gps_idempotency:36000000-0000-4000-8000-000000000001:2026-07-13T01:00:00.000Z',
      'tracking:shuttle:latest:36000000-0000-4000-8000-000000000001',
      'tracking:shuttle:gps_buffer:36000000-0000-4000-8000-000000000001',
      expect.stringMatching(/^[a-f0-9]{64}$/),
      expect.any(String),
      '86400',
      '300',
      '1000',
      '86400',
    );
  });

  it('returns duplicate without recalculating ETA', async () => {
    const client = {
      eval: jest.fn(async () => 0),
      get: jest.fn(),
    };
    const service = new ShuttleService(
      { getClient: jest.fn(() => client) } as unknown as RedisService,
      { sign: jest.fn() } as unknown as TrackingInternalJwtSigner,
      {} as Env,
    );

    const result = await service.recordLocation(createGpsUpdate());

    expect(result).toEqual({ gps: createGpsUpdate(), duplicate: true });
    expect(client.get).not.toHaveBeenCalled();
  });

  it('rejects a reused shuttle operation identity with a different payload', async () => {
    const client = { eval: jest.fn(async () => -1) };
    const service = new ShuttleService(
      { getClient: jest.fn(() => client) } as unknown as RedisService,
      { sign: jest.fn() } as unknown as TrackingInternalJwtSigner,
      {} as Env,
    );

    await expect(service.recordLocation(createGpsUpdate())).rejects.toThrow(
      'GPS_OPERATION_PAYLOAD_MISMATCH',
    );
  });
});

function createGpsUpdate() {
  return {
    shuttleTripId: '36000000-0000-4000-8000-000000000001',
    latitude: 10.77,
    longitude: 106.7,
    speedKmh: 30,
    recordedAt: '2026-07-13T01:00:00.000Z',
  };
}
