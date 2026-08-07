import { Test } from '@nestjs/testing';
import { RedisService } from '@vietride/nest-redis';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import { trackingEtaKey } from '../location/location.constants';
import type { GpsUpdateEvent } from '../location/location.service';
import { ROUTE_GEOMETRY_PROVIDER } from '../off-route/off-route.constants';
import type { RouteGeometryProvider } from '../off-route/route-geometry.provider';
import {
  ETA_CACHE_TTL_SECONDS,
  ETA_STATE_TTL_SECONDS,
  GOOGLE_ETA_PROVIDER,
  LOCAL_ETA_PROVIDER,
  trackingEtaStateKey,
  TRIP_DATA_PROVIDER,
} from './eta.constants';
import type { EtaProvider } from './eta-provider';
import { EtaService } from './eta.service';
import type { TripDataProvider, TripStopSnapshot } from './trip-data.provider';

const TEST_TRIP_ID = '11111111-1111-4111-8111-111111111111';
const TEST_STOP_ID = '22222222-2222-4222-8222-222222222222';
const TEST_DESTINATION_STATION_ID = '33333333-3333-4333-8333-333333333333';

interface RedisMultiMock {
  set: jest.Mock;
  exec: jest.Mock;
}

describe('EtaService', () => {
  let service: EtaService;
  let store: Map<string, string>;
  let redisSet: jest.Mock;
  let redisEval: jest.Mock;
  let multiSet: jest.Mock;
  let localProvider: jest.Mocked<EtaProvider>;
  let googleProvider: jest.Mocked<EtaProvider>;
  let tripDataProvider: jest.Mocked<TripDataProvider>;
  let routePeek: jest.Mock;
  let env: Env;

  beforeEach(async () => {
    store = new Map();
    redisSet = jest.fn(async (key: string, value: string) => {
      store.set(key, value);
      return 'OK';
    });
    redisEval = jest.fn(async () => 1);
    const pendingMultiSets: Array<[string, string]> = [];
    const multi = {} as RedisMultiMock;
    multi.set = jest.fn((key: string, value: string) => {
        pendingMultiSets.push([key, value]);
        return multi;
      });
    multi.exec = jest.fn(async () => {
        for (const [key, value] of pendingMultiSets) store.set(key, value);
        return [];
      });
    multiSet = multi.set;
    localProvider = {
      calculate: jest.fn(async (gps: GpsUpdateEvent, stop: TripStopSnapshot) => {
        void gps;
        void stop;
        return { distanceMeters: 7_500, etaMinutes: 12 };
      }),
    };
    googleProvider = {
      calculate: jest.fn(async (gps: GpsUpdateEvent, stop: TripStopSnapshot) => {
        void gps;
        void stop;
        return { distanceMeters: 7_000, etaMinutes: 10 };
      }),
    };
    env = createEnv();
    tripDataProvider = {
      getRouteStops: jest.fn(async (tripId: string) => {
        void tripId;
        return [createStop()];
      }),
    };
    routePeek = jest.fn(() => ({
        tripId: TEST_TRIP_ID,
        points: [
          { latitude: 10.7, longitude: 106.66 },
          { latitude: 10.9, longitude: 106.66 },
        ],
      }));
    const routeProvider: RouteGeometryProvider = {
      peekCachedRouteGeometry: routePeek,
      getRouteGeometry: async () => null,
    };

    const moduleRef = await Test.createTestingModule({
      providers: [
        EtaService,
        {
          provide: RedisService,
          useValue: {
            getClient: () => ({
              get: async (key: string) => store.get(key) ?? null,
              set: redisSet,
              eval: redisEval,
              multi: () => multi,
            }),
          },
        },
        { provide: TRIP_DATA_PROVIDER, useValue: tripDataProvider },
        { provide: ROUTE_GEOMETRY_PROVIDER, useValue: routeProvider },
        { provide: LOCAL_ETA_PROVIDER, useValue: localProvider },
        { provide: GOOGLE_ETA_PROVIDER, useValue: googleProvider },
        { provide: ENV_TOKEN, useValue: env },
      ],
    }).compile();

    service = moduleRef.get(EtaService);
  });

  it('does not recalculate when movement is below threshold and ETA is not soon', async () => {
    seedState({ latitude: 10.7627, longitude: 106.6602, etaMinutes: 30 });
    seedEta(30);

    await expect(service.handleGpsUpdate(createGps())).resolves.toBeNull();

    expect(localProvider.calculate).not.toHaveBeenCalled();
  });

  it('updates Redis when movement is above threshold', async () => {
    seedState({ latitude: 10.7, longitude: 106.6, etaMinutes: 30 });
    seedEta(30);

    const result = await service.handleGpsUpdate(createGps());

    expect(result).toEqual(expect.objectContaining({ tripId: TEST_TRIP_ID, stopId: TEST_STOP_ID }));
    expect(multiSet).toHaveBeenCalledWith(
      trackingEtaKey(TEST_TRIP_ID, TEST_STOP_ID),
      expect.any(String),
      'EX',
      ETA_CACHE_TTL_SECONDS,
    );
    expect(multiSet).toHaveBeenCalledWith(
      trackingEtaStateKey(TEST_TRIP_ID),
      expect.any(String),
      'EX',
      ETA_STATE_TTL_SECONDS,
    );
    expect(redisEval).toHaveBeenCalledTimes(1);
  });

  it('also caches ETA for the effective destination station without changing the next-stop event', async () => {
    routePeek.mockReturnValue({
      tripId: TEST_TRIP_ID,
      effectiveRouteId: '44444444-4444-4444-8444-444444444444',
      points: [
        { latitude: 10.7, longitude: 106.66 },
        { latitude: 11.9, longitude: 108.44 },
      ],
      destinationStation: {
        stationId: TEST_DESTINATION_STATION_ID,
        name: 'Da Lat',
        latitude: 11.9,
        longitude: 108.44,
      },
    });

    const result = await service.handleGpsUpdate(createGps());

    expect(result).toEqual(expect.objectContaining({ stopId: TEST_STOP_ID }));
    const destinationPayload = store.get(
      trackingEtaKey(TEST_TRIP_ID, TEST_DESTINATION_STATION_ID),
    );
    if (!destinationPayload) throw new Error('Expected destination ETA to be cached');
    const destinationEta = JSON.parse(destinationPayload) as Record<string, unknown>;
    expect(destinationEta).toMatchObject({
      tripId: TEST_TRIP_ID,
      targetKind: 'STATION',
      stationId: TEST_DESTINATION_STATION_ID,
      etaMinutes: 12,
      distanceMeters: 7_500,
    });
    expect(localProvider.calculate).toHaveBeenNthCalledWith(
      2,
      expect.any(Object),
      expect.objectContaining({ stopId: TEST_DESTINATION_STATION_ID, stopName: 'Da Lat' }),
    );
  });

  it('still returns the next-stop event when destination ETA calculation fails', async () => {
    routePeek.mockReturnValue({
      tripId: TEST_TRIP_ID,
      points: [
        { latitude: 10.7, longitude: 106.66 },
        { latitude: 11.9, longitude: 108.44 },
      ],
      destinationStation: {
        stationId: TEST_DESTINATION_STATION_ID,
        name: 'Da Lat',
        latitude: 11.9,
        longitude: 108.44,
      },
    });
    localProvider.calculate
      .mockResolvedValueOnce({ distanceMeters: 7_500, etaMinutes: 12 })
      .mockRejectedValueOnce(new Error('destination provider unavailable'));

    await expect(service.handleGpsUpdate(createGps())).resolves.toEqual(
      expect.objectContaining({ stopId: TEST_STOP_ID, etaMinutes: 12 }),
    );
  });

  it('recalculates after the minimum interval when cached ETA is below 15 minutes', async () => {
    seedState({ latitude: 10.7627, longitude: 106.6602, etaMinutes: 10 });
    seedEta(10);

    await expect(service.handleGpsUpdate(createGps())).resolves.toEqual(expect.objectContaining({ etaMinutes: 12 }));
    expect(localProvider.calculate).toHaveBeenCalledTimes(1);
  });

  it('does not crash GPS realtime when TripDataProvider fails', async () => {
    tripDataProvider.getRouteStops.mockRejectedValue(new Error('trip provider unavailable'));

    await expect(service.handleGpsUpdate(createGps())).resolves.toBeNull();

    expect(localProvider.calculate).not.toHaveBeenCalled();
  });

  it('uses Google as primary when enabled', async () => {
    env.GOOGLE_ROUTES_ENABLED = true;
    env.GOOGLE_ROUTES_API_KEY = 'fake-key';

    await expect(service.handleGpsUpdate(createGps())).resolves.toEqual(expect.objectContaining({ etaMinutes: 10 }));

    expect(googleProvider.calculate).toHaveBeenCalledTimes(1);
    expect(localProvider.calculate).not.toHaveBeenCalled();
  });

  it('uses Google when no route polyline is cached', async () => {
    routePeek.mockReturnValue(null);
    env.GOOGLE_ROUTES_ENABLED = true;
    env.GOOGLE_ROUTES_API_KEY = 'fake-key';

    await expect(service.handleGpsUpdate(createGps())).resolves.toEqual(
      expect.objectContaining({ etaMinutes: 10 }),
    );

    expect(googleProvider.calculate).toHaveBeenCalledTimes(1);
    expect(localProvider.calculate).not.toHaveBeenCalled();
  });

  it('falls back locally and opens cooldown after three Google failures', async () => {
    env.GOOGLE_ROUTES_ENABLED = true;
    env.GOOGLE_ROUTES_API_KEY = 'fake-key';
    googleProvider.calculate.mockResolvedValue(null);

    for (let attempt = 0; attempt < 3; attempt += 1) {
      await expect(service.handleGpsUpdate(createGps())).resolves.toEqual(expect.objectContaining({ etaMinutes: 12 }));
      ageProviderState();
    }

    const cooldownState = parseStoredObject(store.get(trackingEtaStateKey(TEST_TRIP_ID)) ?? '{}');
    expect(googleProvider.calculate).toHaveBeenCalledTimes(3);
    expect(cooldownState['googleFailureCount']).toBe(3);
    expect(new Date(String(cooldownState['cooldownUntil'])).getTime()).toBeGreaterThan(Date.now());

    await service.handleGpsUpdate(createGps());
    expect(googleProvider.calculate).toHaveBeenCalledTimes(3);
    expect(localProvider.calculate).toHaveBeenCalledTimes(4);
  });

  it('does not call an ETA provider when the trip-stop lock is held', async () => {
    redisSet.mockResolvedValueOnce(null);

    await expect(service.handleGpsUpdate(createGps())).resolves.toBeNull();

    expect(googleProvider.calculate).not.toHaveBeenCalled();
    expect(localProvider.calculate).not.toHaveBeenCalled();
  });

  it.each(['ARRIVED', 'SKIPPED'])('ignores %s stops and selects the next pending stop', async (status) => {
    tripDataProvider.getRouteStops.mockResolvedValue([
      createStop({
        stopId: '33333333-3333-4333-8333-333333333333',
        sequence: 1,
        status,
      }),
      createStop({ sequence: 2, status: 'PENDING', latitude: 10.85 }),
    ]);

    const result = await service.handleGpsUpdate(createGps());

    expect(result?.stopId).toBe(TEST_STOP_ID);
    expect(localProvider.calculate).toHaveBeenCalledWith(
      expect.any(Object),
      expect.objectContaining({ status: 'PENDING', sequence: 2 }),
    );
  });

  it('continues selecting PENDING stops', async () => {
    tripDataProvider.getRouteStops.mockResolvedValue([createStop({ status: 'PENDING' })]);

    const result = await service.handleGpsUpdate(createGps());

    expect(result?.stopId).toBe(TEST_STOP_ID);
  });

  it('keeps GPS progress selection when Trip has not deployed stop status yet', async () => {
    tripDataProvider.getRouteStops.mockResolvedValue([
      createStop({
        stopId: '33333333-3333-4333-8333-333333333333',
        latitude: 10.72,
        sequence: 1,
      }),
      createStop({ sequence: 2 }),
    ]);

    const result = await service.handleGpsUpdate(createGps());

    expect(result?.stopId).toBe(TEST_STOP_ID);
    expect(localProvider.calculate).toHaveBeenCalledWith(
      expect.any(Object),
      expect.objectContaining({ sequence: 2 }),
    );
  });

  it('recalculates immediately when the selected stop changes before the 60-second interval', async () => {
    const nextStopId = '33333333-3333-4333-8333-333333333333';
    tripDataProvider.getRouteStops.mockResolvedValue([
      createStop({
        stopId: '44444444-4444-4444-8444-444444444444',
        sequence: 1,
        status: 'ARRIVED',
      }),
      createStop({
        stopId: nextStopId,
        sequence: 2,
        latitude: 10.85,
        status: 'PENDING',
      }),
    ]);
    seedState({
      latitude: 10.7627,
      longitude: 106.6602,
      etaMinutes: 30,
      lastProviderCallAt: new Date(Date.now() - 10_000).toISOString(),
    });

    const result = await service.handleGpsUpdate(createGps());

    expect(result).toEqual(expect.objectContaining({ stopId: nextStopId }));
    expect(localProvider.calculate).toHaveBeenCalledWith(
      expect.any(Object),
      expect.objectContaining({ stopId: nextStopId, sequence: 2 }),
    );
    expect(multiSet).toHaveBeenCalledWith(
      trackingEtaKey(TEST_TRIP_ID, nextStopId),
      expect.any(String),
      'EX',
      ETA_CACHE_TTL_SECONDS,
    );
  });

  it('does not call a provider for a same-stop cache miss before the minimum interval', async () => {
    seedState({
      latitude: 10.7627,
      longitude: 106.6602,
      etaMinutes: 30,
      lastProviderCallAt: new Date(Date.now() - 10_000).toISOString(),
    });

    await expect(service.handleGpsUpdate(createGps())).resolves.toBeNull();

    expect(googleProvider.calculate).not.toHaveBeenCalled();
    expect(localProvider.calculate).not.toHaveBeenCalled();
  });

  it('keeps the 60-second ETA cache TTL when geometry uses the stops-only fallback', async () => {
    routePeek.mockReturnValue({
      tripId: TEST_TRIP_ID,
      geometrySource: 'STOPS_ONLY',
      points: [
        { latitude: 10.7, longitude: 106.66 },
        { latitude: 10.9, longitude: 106.66 },
      ],
    });
    tripDataProvider.getRouteStops.mockResolvedValue([createStop()]);

    await service.handleGpsUpdate(createGps());

    expect(multiSet).toHaveBeenCalledWith(
      trackingEtaKey(TEST_TRIP_ID, TEST_STOP_ID),
      expect.any(String),
      'EX',
      ETA_CACHE_TTL_SECONDS,
    );
  });

  function seedState(
    overrides: Partial<Record<'latitude' | 'longitude' | 'etaMinutes', number>> & { lastProviderCallAt?: string },
  ): void {
    store.set(trackingEtaStateKey(TEST_TRIP_ID), JSON.stringify({
      stopId: TEST_STOP_ID,
      stopSequence: 1,
      lastProviderCallAt: new Date(Date.now() - 61_000).toISOString(),
      googleFailureCount: 0,
      ...overrides,
    }));
  }

  function seedEta(etaMinutes: number): void {
    store.set(trackingEtaKey(TEST_TRIP_ID, TEST_STOP_ID), JSON.stringify({
      tripId: TEST_TRIP_ID,
      stopId: TEST_STOP_ID,
      etaMinutes,
      estimatedArrivalTime: '2026-06-03T10:30:00.000Z',
      distanceMeters: 10_000,
      updatedAt: '2026-06-03T10:00:00.000Z',
    }));
  }

  function ageProviderState(): void {
    const key = trackingEtaStateKey(TEST_TRIP_ID);
    const state = parseStoredObject(store.get(key) ?? '{}');
    state['lastProviderCallAt'] = new Date(Date.now() - 61_000).toISOString();
    store.set(key, JSON.stringify(state));
  }

  function createGps(): GpsUpdateEvent {
    return {
      tripId: TEST_TRIP_ID,
      latitude: 10.762622,
      longitude: 106.660172,
      speedKmh: 40,
      recordedAt: '2026-06-03T10:00:00.000Z',
    };
  }
});

function createStop(overrides: Partial<TripStopSnapshot> = {}): TripStopSnapshot {
  return {
    stopId: TEST_STOP_ID,
    latitude: 10.8231,
    longitude: 106.66,
    sequence: 1,
    estimatedArrivalTime: '2026-06-03T10:30:00.000Z',
    ...overrides,
  };
}

function parseStoredObject(payload: string): Record<string, unknown> {
  const parsed = JSON.parse(payload) as unknown;
  return parsed !== null && typeof parsed === 'object' && !Array.isArray(parsed)
    ? parsed as Record<string, unknown>
    : {};
}

function createEnv(): Env {
  return {
    GOOGLE_ROUTES_ENABLED: false,
    GOOGLE_ROUTES_API_KEY: '',
    TRACKING_ETA_MIN_INTERVAL_SECONDS: 60,
    TRACKING_ETA_CACHE_TTL_SECONDS: 60,
    TRACKING_ETA_FAILURE_COOLDOWN_SECONDS: 300,
  } as Env;
}
