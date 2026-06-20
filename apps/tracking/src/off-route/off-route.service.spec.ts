import { Test } from '@nestjs/testing';
import { RedisService } from '@vietride/nest-redis';
import { TrackingPrismaService } from '../prisma/tracking-prisma.service';
import {
  OFF_ROUTE_EVENT_TYPE,
  OFF_ROUTE_STATE_TTL_SECONDS,
  ROUTE_GEOMETRY_PROVIDER,
  trackingOffRouteSinceKey,
} from './off-route.constants';
import { OffRouteService } from './off-route.service';
import type { RouteGeometryProvider } from './route-geometry.provider';

const TEST_TRIP_ID = '11111111-1111-4111-8111-111111111111';
const ALERT_RECIPIENT_USER_ID = '66666666-6666-4666-8666-666666666666';
const FIRST_RECORDED_AT = '2026-06-04T10:00:00.000Z';
const SHORT_DRIFT_RECORDED_AT = '2026-06-04T10:01:00.000Z';
const ALERT_RECORDED_AT = '2026-06-04T10:02:01.000Z';

describe('OffRouteService', () => {
  let service: OffRouteService;
  let redisGet: jest.MockedFunction<(key: string) => Promise<string | null>>;
  let redisSet: jest.MockedFunction<(key: string, value: string, mode: string, ttl: number) => Promise<string>>;
  let redisDel: jest.MockedFunction<(key: string) => Promise<number>>;
  let outboxCreate: jest.MockedFunction<(args: unknown) => Promise<unknown>>;
  let routeGeometryProvider: jest.Mocked<RouteGeometryProvider>;

  beforeEach(async () => {
    redisGet = jest.fn(async (key: string) => {
      void key;
      return null;
    });
    redisSet = jest.fn(async (key: string, value: string, mode: string, ttl: number) => {
      void key;
      void value;
      void mode;
      void ttl;
      return 'OK';
    });
    redisDel = jest.fn(async (key: string) => {
      void key;
      return 1;
    });
    outboxCreate = jest.fn(async (args: unknown) => args);
    routeGeometryProvider = {
      getRouteGeometry: jest.fn(async (tripId: string) => ({
        tripId,
        alertRecipientUserIds: [ALERT_RECIPIENT_USER_ID],
        points: [
          { latitude: 10.7, longitude: 106.6 },
          { latitude: 10.8, longitude: 106.6 },
        ],
      })),
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
            })),
          },
        },
        {
          provide: TrackingPrismaService,
          useValue: {
            outboxEvent: {
              create: outboxCreate,
            },
          },
        },
        {
          provide: ROUTE_GEOMETRY_PROVIDER,
          useValue: routeGeometryProvider,
        },
      ],
    }).compile();

    service = moduleRef.get(OffRouteService);
  });

  it('does not alert for short GPS drift', async () => {
    redisGet.mockResolvedValueOnce(null).mockResolvedValueOnce(JSON.stringify({ firstDetectedAt: FIRST_RECORDED_AT }));

    await expect(service.handleGpsUpdate(createOffRouteGps(FIRST_RECORDED_AT))).resolves.toBeNull();
    await expect(service.handleGpsUpdate(createOffRouteGps(SHORT_DRIFT_RECORDED_AT))).resolves.toBeNull();

    expect(redisSet).toHaveBeenCalledWith(
      trackingOffRouteSinceKey(TEST_TRIP_ID),
      JSON.stringify({ firstDetectedAt: FIRST_RECORDED_AT }),
      'EX',
      OFF_ROUTE_STATE_TTL_SECONDS,
      'NX',
    );
    expect(outboxCreate).not.toHaveBeenCalled();
  });

  it('creates one alert after continuous off-route threshold', async () => {
    redisGet
      .mockResolvedValueOnce(JSON.stringify({ firstDetectedAt: FIRST_RECORDED_AT }))
      .mockResolvedValueOnce(JSON.stringify({ firstDetectedAt: FIRST_RECORDED_AT, alertedAt: ALERT_RECORDED_AT }));

    await expect(service.handleGpsUpdate(createOffRouteGps(ALERT_RECORDED_AT))).resolves.toEqual(
      expect.objectContaining({
        tripId: TEST_TRIP_ID,
        latitude: 10.75,
        longitude: 106.61,
        detectedAt: ALERT_RECORDED_AT,
      }),
    );
    await expect(service.handleGpsUpdate(createOffRouteGps('2026-06-04T10:03:00.000Z'))).resolves.toBeNull();

    expect(outboxCreate).toHaveBeenCalledTimes(1);
    expect(outboxCreate).toHaveBeenCalledWith({
      data: {
        eventType: OFF_ROUTE_EVENT_TYPE,
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
    });
  });

  it('clears Redis timer when vehicle returns to route', async () => {
    await expect(service.handleGpsUpdate(createOnRouteGps())).resolves.toBeNull();

    expect(redisDel).toHaveBeenCalledWith(trackingOffRouteSinceKey(TEST_TRIP_ID));
    expect(redisSet).not.toHaveBeenCalled();
    expect(outboxCreate).not.toHaveBeenCalled();
  });

  function createOffRouteGps(recordedAt: string) {
    return {
      tripId: TEST_TRIP_ID,
      latitude: 10.75,
      longitude: 106.61,
      recordedAt,
    };
  }

  function createOnRouteGps() {
    return {
      tripId: TEST_TRIP_ID,
      latitude: 10.75,
      longitude: 106.6,
      recordedAt: FIRST_RECORDED_AT,
    };
  }
});
