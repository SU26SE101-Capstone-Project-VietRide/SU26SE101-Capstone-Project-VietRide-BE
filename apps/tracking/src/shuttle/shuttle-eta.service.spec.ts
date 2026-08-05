import { RedisService } from '@vietride/nest-redis';
import type { Env } from '../config/env.schema';
import { GoogleRoutesEtaProvider } from '../eta/google-routes-eta.provider';
import { ShuttleEtaService } from './shuttle-eta.service';
import type { ShuttleTrackingContext } from './shuttle.service';
import {
  shuttleEtaKey,
  shuttleEtaLockKey,
  shuttleEtaStateKey,
} from './shuttle.constants';

const SHUTTLE_ID = '36000000-0000-4000-8000-000000000001';

describe('ShuttleEtaService', () => {
  let store: Map<string, string>;
  let redisSet: jest.Mock;
  let redisEval: jest.Mock;
  let google: jest.Mocked<Pick<GoogleRoutesEtaProvider, 'calculateCoordinates'>>;
  let env: Env;
  let service: ShuttleEtaService;

  beforeEach(() => {
    store = new Map();
    redisSet = jest.fn(async (key: string, value: string) => {
      store.set(key, value);
      return 'OK';
    });
    redisEval = jest.fn(async () => 1);
    google = {
      calculateCoordinates: jest.fn(async (_origin, _destination) => {
        void _origin;
        void _destination;
        return { distanceMeters: 6_200, etaMinutes: 10 };
      }),
    };
    env = {
      GOOGLE_ROUTES_ENABLED: true,
      GOOGLE_ROUTES_API_KEY: 'fake-key',
      TRACKING_ETA_MIN_INTERVAL_SECONDS: 60,
      TRACKING_ETA_CACHE_TTL_SECONDS: 60,
      TRACKING_ETA_FAILURE_COOLDOWN_SECONDS: 300,
    } as Env;
    const multi = createMulti(store);
    service = new ShuttleEtaService(
      {
        getClient: () => ({
          get: async (key: string) => store.get(key) ?? null,
          set: redisSet,
          eval: redisEval,
          multi: () => multi,
        }),
      } as unknown as RedisService,
      google as unknown as GoogleRoutesEtaProvider,
      env,
    );
  });

  it('uses Google Routes for the next non-cancelled pickup and keeps the public ETA shape', async () => {
    const event = await service.handleGpsUpdate(createGps(), createContext());

    expect(google.calculateCoordinates).toHaveBeenCalledWith(
      { latitude: 10.7, longitude: 106.65 },
      { latitude: 10.72, longitude: 106.67 },
    );
    expect(event).toEqual({
      shuttleTripId: SHUTTLE_ID,
      nextPickupOrder: 2,
      etaMinutes: 10,
      estimatedArrivalTime: '2026-08-01T01:10:00.000Z',
      distanceMeters: 6_200,
      updatedAt: expect.any(String),
    });
    expect(store.has(shuttleEtaKey(SHUTTLE_ID, 2))).toBe(true);
  });

  it('uses the local fallback and opens a cooldown after three Google failures', async () => {
    google.calculateCoordinates.mockResolvedValue(null);

    for (let attempt = 0; attempt < 3; attempt += 1) {
      const event = await service.handleGpsUpdate(createGps(), createContext());
      expect(event?.etaMinutes).toBeGreaterThan(0);
      ageState();
    }

    const state = JSON.parse(store.get(shuttleEtaStateKey(SHUTTLE_ID)) ?? '{}');
    expect(state.googleFailureCount).toBe(3);
    expect(new Date(state.cooldownUntil).getTime()).toBeGreaterThan(Date.now());

    ageState();
    await service.handleGpsUpdate(createGps(), createContext());
    expect(google.calculateCoordinates).toHaveBeenCalledTimes(3);
  });

  it('uses the local fallback without calling Google when the shared flag is disabled', async () => {
    env.GOOGLE_ROUTES_ENABLED = false;

    const event = await service.handleGpsUpdate(createGps(), createContext());

    expect(event?.etaMinutes).toBeGreaterThan(0);
    expect(google.calculateCoordinates).not.toHaveBeenCalled();
  });

  it('does not call a provider before the 60-second minimum interval', async () => {
    store.set(shuttleEtaStateKey(SHUTTLE_ID), JSON.stringify({
      order: 2,
      latitude: 10.7,
      longitude: 106.65,
      etaMinutes: 10,
      lastProviderCallAt: new Date().toISOString(),
      googleFailureCount: 0,
    }));
    store.set(shuttleEtaKey(SHUTTLE_ID, 2), JSON.stringify({
      shuttleTripId: SHUTTLE_ID,
      nextPickupOrder: 2,
      etaMinutes: 10,
    }));

    await expect(service.handleGpsUpdate(createGps(), createContext())).resolves.toBeUndefined();
    expect(google.calculateCoordinates).not.toHaveBeenCalled();
  });

  it('does not recalculate after the minimum interval when movement is below 500 m and ETA is not soon', async () => {
    store.set(shuttleEtaStateKey(SHUTTLE_ID), JSON.stringify({
      order: 2,
      latitude: 10.7001,
      longitude: 106.6501,
      etaMinutes: 30,
      lastProviderCallAt: new Date(Date.now() - 61_000).toISOString(),
      googleFailureCount: 0,
    }));
    store.set(shuttleEtaKey(SHUTTLE_ID, 2), JSON.stringify({
      shuttleTripId: SHUTTLE_ID,
      nextPickupOrder: 2,
      etaMinutes: 30,
    }));

    await expect(service.handleGpsUpdate(createGps(), createContext())).resolves.toBeUndefined();
    expect(google.calculateCoordinates).not.toHaveBeenCalled();
  });

  it('does not call a provider while the per-shuttle pickup lock is held', async () => {
    redisSet.mockResolvedValueOnce(null);

    await expect(service.handleGpsUpdate(createGps(), createContext())).resolves.toBeUndefined();

    expect(redisSet).toHaveBeenCalledWith(
      shuttleEtaLockKey(SHUTTLE_ID, 2),
      expect.any(String),
      'EX',
      expect.any(Number),
      'NX',
    );
    expect(google.calculateCoordinates).not.toHaveBeenCalled();
  });

  it('skips terminal pickup groups and targets the final Station', async () => {
    const context = createContext();
    const [firstStop, secondStop] = context.stops;
    if (!firstStop || !secondStop) throw new Error('Expected pickup and Station fixtures');
    context.stops = [
      { ...firstStop, pickupOrder: 1, status: 'PICKED_UP' },
      { ...secondStop, pickupOrder: 2, status: 'DELIVERED' },
      { ...secondStop, pickupOrder: 3, status: 'NO_SHOW' },
      { ...secondStop, pickupOrder: 4, status: 'CANCELLED' },
      {
        pickupOrder: 5,
        bookingId: null,
        latitude: 10.7769,
        longitude: 106.7009,
        status: 'PENDING',
        isStation: true,
      },
    ];

    const event = await service.handleGpsUpdate(createGps(), context);

    expect(event?.nextPickupOrder).toBe(5);
    expect(google.calculateCoordinates).toHaveBeenCalledWith(
      { latitude: 10.7, longitude: 106.65 },
      { latitude: 10.7769, longitude: 106.7009 },
    );
  });

  it('targets the Station first for outbound shuttle ETA even when service stops sort first', async () => {
    const context = createContext();
    context.direction = 'OUTBOUND_FROM_STATION';
    context.stops = [
      {
        pickupOrder: 2,
        bookingId: '36000000-0000-4000-8000-000000000006',
        latitude: 10.9,
        longitude: 106.9,
        status: 'PENDING',
        isStation: false,
      },
      {
        pickupOrder: 1,
        bookingId: null,
        latitude: 10.8,
        longitude: 106.8,
        status: 'PENDING',
        isStation: true,
      },
    ];

    await service.handleGpsUpdate(createGps(), context);

    expect(google.calculateCoordinates).toHaveBeenCalledWith(
      { latitude: 10.7, longitude: 106.65 },
      { latitude: 10.8, longitude: 106.8 },
    );
  });

  it('advances outbound ETA from the departed Station to the first passenger', async () => {
    const context = createContext();
    context.direction = 'OUTBOUND_FROM_STATION';
    context.status = 'IN_PROGRESS';
    context.stops = [
      {
        pickupOrder: 1,
        bookingId: null,
        latitude: 10.8,
        longitude: 106.8,
        status: 'PICKED_UP',
        isStation: true,
      },
      {
        pickupOrder: 2,
        bookingId: '36000000-0000-4000-8000-000000000006',
        latitude: 10.9,
        longitude: 106.9,
        status: 'PENDING',
        isStation: false,
      },
    ];

    await service.handleGpsUpdate(createGps(), context);

    expect(google.calculateCoordinates).toHaveBeenCalledWith(
      { latitude: 10.7, longitude: 106.65 },
      { latitude: 10.9, longitude: 106.9 },
    );
  });

  it('does not regress to a lower pickup order from stale context', async () => {
    const context = createContext();
    context.stops = context.stops.map((stop, index) => ({
      ...stop,
      pickupOrder: index + 2,
      status: 'PENDING',
    }));
    store.set(shuttleEtaStateKey(SHUTTLE_ID), JSON.stringify({
      order: 3,
      latitude: 10.6,
      longitude: 106.5,
      etaMinutes: 30,
      lastProviderCallAt: new Date(Date.now() - 61_000).toISOString(),
      googleFailureCount: 0,
    }));

    const event = await service.handleGpsUpdate(createGps(), context);

    expect(event?.nextPickupOrder).toBe(3);
  });

  function ageState(): void {
    const key = shuttleEtaStateKey(SHUTTLE_ID);
    const state = JSON.parse(store.get(key) ?? '{}');
    state.lastProviderCallAt = new Date(Date.now() - 61_000).toISOString();
    store.set(key, JSON.stringify(state));
  }
});

function createGps() {
  return {
    shuttleTripId: SHUTTLE_ID,
    latitude: 10.7,
    longitude: 106.65,
    speedKmh: 30,
    recordedAt: '2026-08-01T01:00:00.000Z',
  };
}

function createContext(): ShuttleTrackingContext {
  return {
    shuttleTripId: SHUTTLE_ID,
    mainTripId: '36000000-0000-4000-8000-000000000002',
    operatorId: '36000000-0000-4000-8000-000000000003',
    driverUserId: '36000000-0000-4000-8000-000000000004',
    allowed: true,
    scope: 'DRIVER',
    stops: [
      {
        pickupOrder: 1,
        bookingId: '36000000-0000-4000-8000-000000000005',
        latitude: 10.71,
        longitude: 106.66,
        status: 'CANCELLED',
        isStation: false,
      },
      {
        pickupOrder: 2,
        bookingId: '36000000-0000-4000-8000-000000000006',
        latitude: 10.72,
        longitude: 106.67,
        status: 'PENDING',
        isStation: false,
      },
    ],
  };
}

interface MultiMock {
  set: jest.Mock;
  exec: jest.Mock;
}

function createMulti(store: Map<string, string>): MultiMock {
  const pending: Array<[string, string]> = [];
  const multi = {} as MultiMock;
  multi.set = jest.fn((key: string, value: string): MultiMock => {
      pending.push([key, value]);
      return multi;
    });
  multi.exec = jest.fn(async () => {
    for (const [key, value] of pending.splice(0)) store.set(key, value);
    return [];
  });
  return multi;
}
