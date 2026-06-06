import { Test } from '@nestjs/testing';
import { RedisService } from '@vietride/nest-redis';
import { trackingEtaKey } from '../location/location.constants';
import type { GpsUpdateEvent } from '../location/location.service';
import {
  ETA_CACHE_TTL_SECONDS,
  trackingEtaStateKey,
  TRIP_DATA_PROVIDER,
} from './eta.constants';
import { EtaService } from './eta.service';
import type { TripDataProvider, TripStopSnapshot } from './trip-data.provider';

const TEST_TRIP_ID = '11111111-1111-4111-8111-111111111111';
const TEST_STOP_ID = '22222222-2222-4222-8222-222222222222';

interface RedisMultiMock {
  set: jest.MockedFunction<(key: string, value: string, mode: string, ttl: number) => RedisMultiMock>;
  exec: jest.MockedFunction<() => Promise<unknown[]>>;
}

describe('EtaService', () => {
  let service: EtaService;
  let redisGet: jest.MockedFunction<(key: string) => Promise<string | null>>;
  let redisMulti: RedisMultiMock;
  let tripDataProvider: jest.Mocked<TripDataProvider>;

  beforeEach(async () => {
    redisGet = jest.fn(async (key: string) => {
      void key;
      return null;
    });
    redisMulti = createRedisMultiMock();
    tripDataProvider = {
      getRouteStops: jest.fn(async (tripId: string) => {
        void tripId;
        return [createStop()];
      }),
    };

    const moduleRef = await Test.createTestingModule({
      providers: [
        EtaService,
        {
          provide: RedisService,
          useValue: {
            getClient: jest.fn(() => ({
              get: redisGet,
              multi: jest.fn(() => redisMulti),
            })),
          },
        },
        {
          provide: TRIP_DATA_PROVIDER,
          useValue: tripDataProvider,
        },
      ],
    }).compile();

    service = moduleRef.get(EtaService);
  });

  it('does not recalculate when movement is below threshold and ETA is not soon', async () => {
    redisGet.mockResolvedValue(JSON.stringify({
      latitude: 10.7627,
      longitude: 106.6602,
      etaMinutes: 30,
      stopId: TEST_STOP_ID,
    }));

    await expect(service.handleGpsUpdate(createGps())).resolves.toBeNull();

    expect(redisMulti.exec).not.toHaveBeenCalled();
  });

  it('updates Redis when movement is above threshold', async () => {
    redisGet.mockResolvedValue(JSON.stringify({
      latitude: 10.7000,
      longitude: 106.6000,
      etaMinutes: 30,
      stopId: TEST_STOP_ID,
    }));

    const result = await service.handleGpsUpdate(createGps());

    expect(result).toEqual(expect.objectContaining({
      tripId: TEST_TRIP_ID,
      stopId: TEST_STOP_ID,
    }));
    expect(redisMulti.set).toHaveBeenCalledWith(
      trackingEtaKey(TEST_TRIP_ID, TEST_STOP_ID),
      expect.any(String),
      'EX',
      ETA_CACHE_TTL_SECONDS,
    );
    expect(redisMulti.set).toHaveBeenCalledWith(
      trackingEtaStateKey(TEST_TRIP_ID),
      expect.any(String),
      'EX',
      ETA_CACHE_TTL_SECONDS,
    );
    expect(redisMulti.exec).toHaveBeenCalledTimes(1);
  });

  it('recalculates when cached ETA is below the soon threshold', async () => {
    redisGet.mockResolvedValue(JSON.stringify({
      latitude: 10.7627,
      longitude: 106.6602,
      etaMinutes: 10,
      stopId: TEST_STOP_ID,
    }));

    const result = await service.handleGpsUpdate(createGps());

    expect(result?.etaMinutes).toBeGreaterThan(0);
    expect(redisMulti.exec).toHaveBeenCalledTimes(1);
  });

  it('does not crash GPS realtime when TripDataProvider fails', async () => {
    tripDataProvider.getRouteStops.mockRejectedValue(new Error('trip provider unavailable'));

    await expect(service.handleGpsUpdate(createGps())).resolves.toBeNull();

    expect(redisMulti.exec).not.toHaveBeenCalled();
  });

  function createGps(): GpsUpdateEvent {
    return {
      tripId: TEST_TRIP_ID,
      latitude: 10.762622,
      longitude: 106.660172,
      speedKmh: 40,
      recordedAt: '2026-06-03T10:00:00.000Z',
    };
  }

  function createStop(): TripStopSnapshot {
    return {
      stopId: TEST_STOP_ID,
      latitude: 10.8231,
      longitude: 106.6297,
      sequence: 1,
      estimatedArrivalTime: '2026-06-03T10:30:00.000Z',
    };
  }
});

function createRedisMultiMock(): RedisMultiMock {
  const multi = {} as RedisMultiMock;
  multi.set = jest.fn((key: string, value: string, mode: string, ttl: number) => {
    void key;
    void value;
    void mode;
    void ttl;
    return multi;
  });
  multi.exec = jest.fn(async () => []);
  return multi;
}
