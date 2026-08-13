import { Test } from '@nestjs/testing';
import { RedisService } from '@vietride/nest-redis';
import { TrackingPrismaService } from '../prisma/tracking-prisma.service';
import { RouteStateGenerationRegistry } from '../route-state/route-state-generation.registry';
import {
  OFF_ROUTE_EVENT_TYPE,
  OFF_ROUTE_LOCK_TTL_SECONDS,
  OFF_ROUTE_STATE_TTL_SECONDS,
  ROUTE_GEOMETRY_PROVIDER,
  trackingOffRouteLockKey,
  trackingOffRouteSinceKey,
} from './off-route.constants';
import { OffRouteService } from './off-route.service';
import type { RouteGeometryProvider } from './route-geometry.provider';

const TEST_TRIP_ID = '11111111-1111-4111-8111-111111111111';
const ALERT_RECIPIENT_USER_ID = '66666666-6666-4666-8666-666666666666';
const FIRST_RECORDED_AT = '2026-06-04T10:00:00.000Z';
const EXACT_THRESHOLD_RECORDED_AT = '2026-06-04T10:02:00.000Z';
const ALERT_RECORDED_AT = '2026-06-04T10:02:01.000Z';

describe('OffRouteService', () => {
  let service: OffRouteService;
  let values: Map<string, string>;
  let redisGet: jest.Mock;
  let redisSet: jest.Mock;
  let redisDel: jest.Mock;
  let redisEval: jest.Mock;
  let outboxUpsert: jest.Mock;
  let routeGeometryProvider: jest.Mocked<RouteGeometryProvider>;
  let routeStateGeneration: RouteStateGenerationRegistry;

  beforeEach(async () => {
    values = new Map();
    redisGet = jest.fn(async (key: string) => values.get(key) ?? null);
    redisSet = jest.fn(async (...args: unknown[]) => {
      const [key, value, , , condition] = args as [string, string, string, number, string?];
      if (condition === 'NX' && values.has(key)) return null;
      values.set(key, value);
      return 'OK';
    });
    redisDel = jest.fn(async (...keys: string[]) => {
      let deleted = 0;
      for (const key of keys) deleted += values.delete(key) ? 1 : 0;
      return deleted;
    });
    redisEval = jest.fn(async (_script: string, _keyCount: number, key: string, owner: string) => {
      if (values.get(key) !== owner) return 0;
      values.delete(key);
      return 1;
    });
    outboxUpsert = jest.fn(async (args: unknown) => args);
    routeGeometryProvider = {
      peekCachedRouteGeometry: jest.fn((tripId: string) => ({
        tripId,
        alertRecipientUserIds: [ALERT_RECIPIENT_USER_ID],
        points: [
          { latitude: 10.7, longitude: 106.6 },
          { latitude: 10.8, longitude: 106.6 },
        ],
      })),
      getRouteGeometry: jest.fn(async (tripId: string) => {
        void tripId;
        return null;
      }),
      invalidateRouteGeometry: jest.fn(),
    };

    const moduleRef = await Test.createTestingModule({
      providers: [
        OffRouteService,
        {
          provide: RedisService,
          useValue: {
            getClient: jest.fn(() => ({
              get: redisGet,
              set: redisSet,
              del: redisDel,
              eval: redisEval,
            })),
          },
        },
        {
          provide: TrackingPrismaService,
          useValue: { outboxEvent: { upsert: outboxUpsert } },
        },
        { provide: ROUTE_GEOMETRY_PROVIDER, useValue: routeGeometryProvider },
        RouteStateGenerationRegistry,
      ],
    }).compile();

    service = moduleRef.get(OffRouteService);
    routeStateGeneration = moduleRef.get(RouteStateGenerationRegistry);
  });

  it('starts a timer and does not alert at exactly the continuous threshold', async () => {
    await expect(service.handleGpsUpdate(createOffRouteGps(FIRST_RECORDED_AT))).resolves.toBeNull();
    await expect(service.handleGpsUpdate(createOffRouteGps(EXACT_THRESHOLD_RECORDED_AT))).resolves.toBeNull();

    expect(readState()).toEqual({ firstDetectedAt: FIRST_RECORDED_AT });
    expect(outboxUpsert).not.toHaveBeenCalled();
    expect(redisSet).toHaveBeenCalledWith(
      trackingOffRouteSinceKey(TEST_TRIP_ID),
      JSON.stringify({ firstDetectedAt: FIRST_RECORDED_AT }),
      'EX',
      OFF_ROUTE_STATE_TTL_SECONDS,
    );
  });

  it('creates one alert and DEVIATED transition after the threshold', async () => {
    seedState({ firstDetectedAt: FIRST_RECORDED_AT });

    await expect(service.handleGpsUpdate(createOffRouteGps(ALERT_RECORDED_AT))).resolves.toEqual({
      tripId: TEST_TRIP_ID,
      status: 'DEVIATED',
      distanceMeters: expect.any(Number),
      updatedAt: ALERT_RECORDED_AT,
    });

    expect(outboxUpsert).toHaveBeenCalledTimes(1);
    expect(outboxUpsert).toHaveBeenCalledWith({
      where: { dedupeKey: `off-route:${TEST_TRIP_ID}:${FIRST_RECORDED_AT}` },
      create: {
        eventType: OFF_ROUTE_EVENT_TYPE,
        dedupeKey: `off-route:${TEST_TRIP_ID}:${FIRST_RECORDED_AT}`,
        payload: {
          tripId: TEST_TRIP_ID,
          userIds: [ALERT_RECIPIENT_USER_ID],
          latitude: 10.75,
          longitude: 106.61,
          distanceMeters: expect.any(Number),
          durationSeconds: 121,
          detectedAt: ALERT_RECORDED_AT,
        },
      },
      update: {},
    });
    expect(readState()).toEqual({
      firstDetectedAt: FIRST_RECORDED_AT,
      alertedAt: ALERT_RECORDED_AT,
      lastRealtimeEmittedAt: ALERT_RECORDED_AT,
    });
  });

  it('emits no heartbeat before 60 seconds and one heartbeat at 60 seconds', async () => {
    seedState({
      firstDetectedAt: FIRST_RECORDED_AT,
      alertedAt: ALERT_RECORDED_AT,
      lastRealtimeEmittedAt: ALERT_RECORDED_AT,
    });

    await expect(service.handleGpsUpdate(createOffRouteGps('2026-06-04T10:03:00.000Z')))
      .resolves.toBeNull();
    await expect(service.handleGpsUpdate(createOffRouteGps('2026-06-04T10:03:01.000Z')))
      .resolves.toEqual({
        tripId: TEST_TRIP_ID,
        status: 'DEVIATED',
        distanceMeters: expect.any(Number),
        updatedAt: '2026-06-04T10:03:01.000Z',
      });

    expect(outboxUpsert).not.toHaveBeenCalled();
    expect(readState()).toEqual(expect.objectContaining({
      lastRealtimeEmittedAt: '2026-06-04T10:03:01.000Z',
    }));
  });

  it('clears a pre-alert timer without emitting restored', async () => {
    seedState({ firstDetectedAt: FIRST_RECORDED_AT });

    await expect(service.handleGpsUpdate(createOnRouteGps('2026-06-04T10:01:00.000Z')))
      .resolves.toBeNull();

    expect(values.has(trackingOffRouteSinceKey(TEST_TRIP_ID))).toBe(false);
  });

  it('emits ROUTE_RESTORED once after an alerted deviation', async () => {
    seedState({
      firstDetectedAt: FIRST_RECORDED_AT,
      alertedAt: ALERT_RECORDED_AT,
      lastRealtimeEmittedAt: ALERT_RECORDED_AT,
    });

    await expect(service.handleGpsUpdate(createOnRouteGps('2026-06-04T10:04:00.000Z')))
      .resolves.toEqual({
        tripId: TEST_TRIP_ID,
        status: 'ROUTE_RESTORED',
        distanceMeters: 0,
        updatedAt: '2026-06-04T10:04:00.000Z',
      });
    await expect(service.handleGpsUpdate(createOnRouteGps('2026-06-04T10:04:05.000Z')))
      .resolves.toBeNull();
  });

  it('allows only one concurrent evaluation to acquire the trip lock', async () => {
    seedState({ firstDetectedAt: FIRST_RECORDED_AT });
    let releaseRead: (() => void) | undefined;
    redisGet.mockImplementationOnce(async (key: string) => {
      await new Promise<void>((resolve) => { releaseRead = resolve; });
      return values.get(key) ?? null;
    });

    const first = service.handleGpsUpdate(createOffRouteGps(ALERT_RECORDED_AT));
    await Promise.resolve();
    const second = service.handleGpsUpdate(createOffRouteGps(ALERT_RECORDED_AT));
    await expect(second).resolves.toBeNull();
    releaseRead?.();
    await expect(first).resolves.toEqual(expect.objectContaining({ status: 'DEVIATED' }));

    expect(outboxUpsert).toHaveBeenCalledTimes(1);
  });

  it('does not write state or Outbox after route generation invalidation', async () => {
    seedState({ firstDetectedAt: FIRST_RECORDED_AT });
    let releaseRead: (() => void) | undefined;
    let signalReadStarted: (() => void) | undefined;
    const readStarted = new Promise<void>((resolve) => { signalReadStarted = resolve; });
    redisGet.mockImplementationOnce(async (key: string) => {
      signalReadStarted?.();
      await new Promise<void>((resolve) => { releaseRead = resolve; });
      return values.get(key) ?? null;
    });

    const evaluation = service.handleGpsUpdate(createOffRouteGps(ALERT_RECORDED_AT));
    await readStarted;
    routeStateGeneration.invalidate(TEST_TRIP_ID);
    releaseRead?.();

    await expect(evaluation).resolves.toBeNull();
    expect(outboxUpsert).not.toHaveBeenCalled();
  });

  it('fails open on cache miss and warms geometry asynchronously', async () => {
    routeGeometryProvider.peekCachedRouteGeometry.mockReturnValueOnce(null);

    await expect(service.handleGpsUpdate(createOffRouteGps(ALERT_RECORDED_AT))).resolves.toBeNull();

    expect(routeGeometryProvider.getRouteGeometry).toHaveBeenCalledWith(TEST_TRIP_ID);
    expect(redisSet).not.toHaveBeenCalled();
  });

  it('clears runtime state under the same per-trip lock', async () => {
    seedState({ firstDetectedAt: FIRST_RECORDED_AT });

    await expect(service.clearRuntimeState(TEST_TRIP_ID)).resolves.toBeUndefined();

    expect(values.has(trackingOffRouteSinceKey(TEST_TRIP_ID))).toBe(false);
    expect(redisSet).toHaveBeenCalledWith(
      trackingOffRouteLockKey(TEST_TRIP_ID),
      expect.any(String),
      'EX',
      OFF_ROUTE_LOCK_TTL_SECONDS,
      'NX',
    );
  });

  function seedState(state: Record<string, string>): void {
    values.set(trackingOffRouteSinceKey(TEST_TRIP_ID), JSON.stringify(state));
  }

  function readState(): unknown {
    const value = values.get(trackingOffRouteSinceKey(TEST_TRIP_ID));
    return value ? JSON.parse(value) : null;
  }

  function createOffRouteGps(recordedAt: string) {
    return { tripId: TEST_TRIP_ID, latitude: 10.75, longitude: 106.61, recordedAt };
  }

  function createOnRouteGps(recordedAt: string) {
    return { tripId: TEST_TRIP_ID, latitude: 10.75, longitude: 106.6, recordedAt };
  }
});
