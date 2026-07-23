import { RedisService } from '@vietride/nest-redis';
import type { Env } from '../config/env.schema';
import type { TrackingInternalJwtSigner } from '../authorization/tracking-internal-jwt.signer';
import { ShuttleService } from './shuttle.service';

describe('ShuttleService', () => {
  it('writes only shuttle Redis keys and emits ETA for the next pickup', async () => {
    const multi = createMulti();
    const client = {
      eval: jest.fn(async () => 1),
      multi: jest.fn(() => multi),
      get: jest.fn(async () => null),
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
      {
        shuttleTripId: '36000000-0000-4000-8000-000000000001',
        mainTripId: '36000000-0000-4000-8000-000000000002',
        operatorId: '36000000-0000-4000-8000-000000000003',
        driverUserId: '36000000-0000-4000-8000-000000000004',
        allowed: true,
        scope: 'DRIVER',
        stops: [
          {
            pickupOrder: 1,
            bookingId: '36000000-0000-4000-8000-000000000005',
            latitude: 10.78,
            longitude: 106.71,
            status: 'PENDING',
            isStation: false,
          },
        ],
      },
    );

    expect(result.eta?.nextPickupOrder).toBe(1);
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
    expect((multi as Record<string, unknown>).sadd).toBeUndefined();
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

    const result = await service.recordLocation(createGpsUpdate(), createContext());

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

    await expect(service.recordLocation(createGpsUpdate(), createContext())).rejects.toThrow(
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

function createContext() {
  return {
    shuttleTripId: '36000000-0000-4000-8000-000000000001',
    mainTripId: '36000000-0000-4000-8000-000000000002',
    operatorId: '36000000-0000-4000-8000-000000000003',
    driverUserId: '36000000-0000-4000-8000-000000000004',
    allowed: true,
    scope: 'DRIVER',
    stops: [],
  };
}

function createMulti(): {
  set: jest.Mock;
  rpush: jest.Mock;
  ltrim: jest.Mock;
  expire: jest.Mock;
  exec: jest.Mock;
} {
  const multi = {
    set: jest.fn(),
    rpush: jest.fn(),
    ltrim: jest.fn(),
    expire: jest.fn(),
    exec: jest.fn(async () => []),
  };
  multi.set.mockReturnValue(multi);
  multi.rpush.mockReturnValue(multi);
  multi.ltrim.mockReturnValue(multi);
  multi.expire.mockReturnValue(multi);
  return multi;
}
