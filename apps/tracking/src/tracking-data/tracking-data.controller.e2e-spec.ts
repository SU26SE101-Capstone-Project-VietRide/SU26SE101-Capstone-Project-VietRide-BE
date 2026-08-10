import { INestApplication, NotFoundException, ServiceUnavailableException } from '@nestjs/common';
import { APP_FILTER, APP_INTERCEPTOR } from '@nestjs/core';
import { DocumentBuilder, SwaggerModule } from '@nestjs/swagger';
import { Test } from '@nestjs/testing';
import {
  ApiResponseExceptionFilter,
  ApiResponseInterceptor,
} from '@vietride/nest-common';
import { RedisService } from '@vietride/nest-redis';
import { exportSPKI, generateKeyPair, SignJWT, type KeyLike } from 'jose';
import {
  ENV_TOKEN,
  TRACKING_AUTHORIZATION_ADAPTER,
  TRACKING_JWT_VERIFIER,
} from '../app/tokens';
import type { TrackingUser } from '../auth/tracking-user.types';
import { JoseUserJwtVerifier } from '../auth/user-jwt.verifier';
import type {
  TrackingAuthorizationAdapter,
  TrackingAuthorizationResult,
} from '../authorization/tracking-authorization.adapter';
import type { Env } from '../config/env.schema';
import { trackingEtaKey, trackingLatestKey } from '../location/location.constants';
import { trackingTripDelayStateKey } from '../trip-delay/trip-delay.constants';
import { TrackingPrismaService } from '../prisma/tracking-prisma.service';
import { TrackingDataController } from './tracking-data.controller';
import { TrackingDataRepository } from './tracking-data.repository';
import { TrackingDataAuthGuard } from './tracking-data-auth.guard';
import { TrackingDataService } from './tracking-data.service';
import { TripRouteContextService } from './trip-route-context.service';

const TEST_TRIP_ID = '11111111-1111-4111-8111-111111111111';
const TEST_STOP_ID = '22222222-2222-4222-8222-222222222222';
const TEST_USER_ID = '33333333-3333-4333-8333-333333333333';
const UNAUTHORIZED_USER_ID = '44444444-4444-4444-8444-444444444444';
const IDENTITY_ISSUER = 'vietride-identity';
const IDENTITY_AUDIENCE = 'vietride-api';

interface ApiEnvelope<TData> {
  success: boolean;
  statusCode: number;
  data?: TData;
  error?: {
    code: string;
    message: string;
  };
}

describe('TrackingDataController REST fallback (e2e)', () => {
  let app: INestApplication;
  let port: number;
  let privateKey: KeyLike;
  let redisGet: jest.MockedFunction<(key: string) => Promise<string | null>>;
  let prismaFindMany: jest.MockedFunction<(args: unknown) => Promise<unknown[]>>;
  let prismaCount: jest.MockedFunction<(args: unknown) => Promise<number>>;
  let routeContextGet: jest.MockedFunction<TripRouteContextService['getRouteContext']>;
  let delayStatePayload: string | null;
  let etaPayload: Record<string, unknown>;

  beforeAll(async () => {
    const generated = await generateKeyPair('RS256');
    privateKey = generated.privateKey;
    const publicKeyPem = await exportSPKI(generated.publicKey);
    delayStatePayload = null;
    etaPayload = {
      tripId: TEST_TRIP_ID,
      stopId: TEST_STOP_ID,
      etaMinutes: 12,
      estimatedArrivalTime: '2026-06-03T10:13:00.000Z',
      distanceMeters: 8500,
      updatedAt: '2026-06-03T10:01:00.000Z',
    };

    redisGet = jest.fn(async (key: string) => {
      if (key === trackingLatestKey(TEST_TRIP_ID)) {
        return JSON.stringify({
          tripId: TEST_TRIP_ID,
          latitude: 10.762622,
          longitude: 106.660172,
          speedKmh: 42,
          headingDeg: 90,
          recordedAt: '2026-06-03T10:00:00.000Z',
        });
      }
      if (key === trackingEtaKey(TEST_TRIP_ID, TEST_STOP_ID)) {
        return JSON.stringify(etaPayload);
      }
      if (key === trackingTripDelayStateKey(TEST_TRIP_ID)) return delayStatePayload;
      return null;
    });

    const trailRows = [
      {
        id: '55555555-5555-4555-8555-555555555555',
        tripId: TEST_TRIP_ID,
        latitude: '10.7626220',
        longitude: '106.6601720',
        speedKmh: '40.00',
        headingDeg: '89.00',
        recordedAt: new Date('2026-06-03T10:00:00.000Z'),
      },
      {
        id: '66666666-6666-4666-8666-666666666666',
        tripId: TEST_TRIP_ID,
        latitude: '10.7630000',
        longitude: '106.6610000',
        speedKmh: null,
        headingDeg: null,
        recordedAt: new Date('2026-06-03T10:05:00.000Z'),
      },
    ];

    prismaFindMany = jest.fn(async (args: unknown) => {
      void args;
      return trailRows;
    });

    prismaCount = jest.fn(async (args: unknown) => {
      void args;
      return trailRows.length;
    });

    routeContextGet = jest.fn(async (tripId: string) => {
      void tripId;
      return {
        etag: '"route-context-etag"',
        data: {
          tripId: TEST_TRIP_ID,
          geometry: {
            source: 'ROUTE_POLYLINE' as const,
            points: [{ latitude: 10, longitude: 106 }, { latitude: 10.1, longitude: 106.1 }],
          },
          originStation: null,
          intermediateStops: [],
          destinationStation: null,
        },
      };
    });

    const moduleRef = await Test.createTestingModule({
      controllers: [TrackingDataController],
      providers: [
        TrackingDataService,
        {
          provide: TripRouteContextService,
          useValue: { getRouteContext: routeContextGet },
        },
        TrackingDataRepository,
        TrackingDataAuthGuard,
        { provide: ENV_TOKEN, useValue: createTestEnv(publicKeyPem) },
        { provide: TRACKING_JWT_VERIFIER, useClass: JoseUserJwtVerifier },
        { provide: TRACKING_AUTHORIZATION_ADAPTER, useClass: E2eTrackingAuthorizationAdapter },
        {
          provide: RedisService,
          useValue: { getClient: jest.fn(() => ({ get: redisGet })) },
        },
        {
          provide: TrackingPrismaService,
          useValue: { gpsTrail: { findMany: prismaFindMany, count: prismaCount } },
        },
        { provide: APP_FILTER, useValue: new ApiResponseExceptionFilter() },
        { provide: APP_INTERCEPTOR, useValue: new ApiResponseInterceptor() },
      ],
    }).compile();

    app = moduleRef.createNestApplication();
        await app.listen(0);
    port = readListeningPort(app);
  });

  afterAll(async () => {
    if (app) await app.close();
  });

  it('returns 401 envelope when auth is missing', async () => {
    const response = await getJson<ApiEnvelope<unknown>>(`/v1/tracking/trips/${TEST_TRIP_ID}/latest`);

    expect(response.status).toBe(401);
    expect(response.body.success).toBe(false);
    expect(response.body.error?.code).toBe('UNAUTHORIZED');
  });

  it('returns 401 envelope when token is invalid', async () => {
    const response = await getJson<ApiEnvelope<unknown>>(
      `/v1/tracking/trips/${TEST_TRIP_ID}/latest`,
      'not-a-jwt',
    );

    expect(response.status).toBe(401);
    expect(response.body.error?.code).toBe('UNAUTHORIZED');
  });

  it('returns 400 envelope when tripId is not a valid UUID even with valid token', async () => {
    const token = await signIdentityToken('PASSENGER', TEST_USER_ID);
    const response = await getJson<ApiEnvelope<unknown>>(
      '/v1/tracking/trips/not-a-uuid/latest',
      token,
    );

    expect(response.status).toBe(400);
    expect(response.body.success).toBe(false);
    expect(response.body.error?.code).toBe('VALIDATION_FAILED');
  });

  it('returns 403 envelope when trip authorization denies access', async () => {
    const token = await signIdentityToken('PASSENGER', UNAUTHORIZED_USER_ID);
    const response = await getJson<ApiEnvelope<unknown>>(
      `/v1/tracking/trips/${TEST_TRIP_ID}/latest`,
      token,
    );

    expect(response.status).toBe(403);
    expect(response.body.error?.code).toBe('ACCESS_DENIED');
  });

  it('returns latest tracking data from Redis', async () => {
    const token = await signIdentityToken('PASSENGER', TEST_USER_ID);
    const response = await getJson<ApiEnvelope<{ latest: { tripId: string; latitude: number } }>>(
      `/v1/tracking/trips/${TEST_TRIP_ID}/latest`,
      token,
    );

    expect(response.status).toBe(200);
    expect(response.body.success).toBe(true);
    expect(response.body.data?.latest).toEqual(
      expect.objectContaining({
        tripId: TEST_TRIP_ID,
        latitude: 10.762622,
      }),
    );
  });

  it('returns authorized route context with private cache headers and strong ETag', async () => {
    const token = await signIdentityToken('PASSENGER', TEST_USER_ID);
    const response = await fetch(`http://127.0.0.1:${port}/v1/tracking/trips/${TEST_TRIP_ID}/route-geometry`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    const body = (await response.json()) as ApiEnvelope<{
      geometry: { source: string; points: unknown[] };
    }>;

    expect(response.status).toBe(200);
    expect(response.headers.get('cache-control')).toBe('private, max-age=600');
    expect(response.headers.get('vary')).toBe('Authorization');
    expect(response.headers.get('etag')).toBe('"route-context-etag"');
    expect(body.data?.geometry.source).toBe('ROUTE_POLYLINE');
    expect(JSON.stringify(body)).not.toContain('alertRecipientUserIds');
  });

  it('runs authorization before returning an empty 304 route response', async () => {
    const token = await signIdentityToken('PASSENGER', TEST_USER_ID);
    const response = await fetch(`http://127.0.0.1:${port}/v1/tracking/trips/${TEST_TRIP_ID}/route-geometry`, {
      headers: {
        Authorization: `Bearer ${token}`,
        'If-None-Match': '"route-context-etag"',
      },
    });

    expect(response.status).toBe(304);
    expect(await response.text()).toBe('');
    expect(response.headers.get('etag')).toBe('"route-context-etag"');

    const deniedToken = await signIdentityToken('PASSENGER', UNAUTHORIZED_USER_ID);
    const denied = await fetch(`http://127.0.0.1:${port}/v1/tracking/trips/${TEST_TRIP_ID}/route-geometry`, {
      headers: {
        Authorization: `Bearer ${deniedToken}`,
        'If-None-Match': '"route-context-etag"',
      },
    });
    expect(denied.status).toBe(403);
  });

  it('returns 200 with null geometry and preserved markers when no route polyline exists', async () => {
    routeContextGet.mockResolvedValueOnce({
      etag: '"stops-only-etag"',
      data: {
        tripId: TEST_TRIP_ID,
        geometry: null,
        originStation: {
          stationId: '77777777-7777-4777-8777-777777777777',
          name: 'Origin',
          latitude: 10,
          longitude: 106,
        },
        intermediateStops: [],
        destinationStation: null,
      },
    });
    const token = await signIdentityToken('PASSENGER', TEST_USER_ID);
    const response = await fetch(`http://127.0.0.1:${port}/v1/tracking/trips/${TEST_TRIP_ID}/route-geometry`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    const body = (await response.json()) as ApiEnvelope<{
      geometry: null;
      originStation: { stationId: string };
    }>;

    expect(response.status).toBe(200);
    expect(response.headers.get('cache-control')).toBe('private, max-age=30');
    expect(body.data?.geometry).toBeNull();
    expect(body.data?.originStation.stationId).toBe('77777777-7777-4777-8777-777777777777');
  });

  it.each([
    [new NotFoundException({ errorCode: 'TRIP_NOT_FOUND', detail: 'Trip not found' }), 404, 'TRIP_NOT_FOUND'],
    [new ServiceUnavailableException({
      errorCode: 'TRACKING_ROUTE_CONTEXT_UNAVAILABLE',
      detail: 'Route provider unavailable',
    }), 503, 'TRACKING_ROUTE_CONTEXT_UNAVAILABLE'],
  ])('returns the route context error envelope', async (error, status, errorCode) => {
    routeContextGet.mockRejectedValueOnce(error);
    const token = await signIdentityToken('PASSENGER', TEST_USER_ID);
    const response = await getJson<ApiEnvelope<unknown>>(
      `/v1/tracking/trips/${TEST_TRIP_ID}/route-geometry`,
      token,
    );

    expect(response.status).toBe(status);
    expect(response.body.error?.code).toBe(errorCode);
  });

  it('documents every public route context response status in Swagger metadata', () => {
    const document = SwaggerModule.createDocument(app, new DocumentBuilder().build());
    const responses = document.paths[`/v1/tracking/trips/{tripId}/route-geometry`]?.get?.responses;

    expect(Object.keys(responses ?? {})).toEqual(
      expect.arrayContaining(['200', '304', '400', '401', '403', '404', '503']),
    );
  });

  it('returns latest null when Redis has no latest point', async () => {
    const token = await signIdentityToken('PASSENGER', TEST_USER_ID);
    const missingTripId = '77777777-7777-4777-8777-777777777777';
    const response = await getJson<ApiEnvelope<{ latest: null }>>(
      `/v1/tracking/trips/${missingTripId}/latest`,
      token,
    );

    expect(response.status).toBe(200);
    expect(response.body.data?.latest).toBeNull();
  });

  it('returns an empty ETA batch on cold cache without synchronous calculation', async () => {
    const token = await signIdentityToken('PASSENGER', TEST_USER_ID);
    const response = await getJson<ApiEnvelope<{ etas: unknown[] }>>(
      `/v1/tracking/trips/${TEST_TRIP_ID}/etas`,
      token,
    );

    expect(response.status).toBe(200);
    expect(response.body.data?.etas).toEqual([]);
  });

  it('uses the existing trip authorization for the ETA batch endpoint', async () => {
    const token = await signIdentityToken('PASSENGER', UNAUTHORIZED_USER_ID);
    const response = await getJson<ApiEnvelope<unknown>>(
      `/v1/tracking/trips/${TEST_TRIP_ID}/etas`,
      token,
    );

    expect(response.status).toBe(403);
    expect(response.body.error?.code).toBe('ACCESS_DENIED');
  });

  it('returns trail points with pagination metadata', async () => {
    const token = await signIdentityToken('PASSENGER', TEST_USER_ID);
    const response = await getJson<ApiEnvelope<{
      items: Array<{ recordedAt: string }>;
      page: number;
      pageSize: number;
      totalItems: number;
      totalPages: number;
      hasNextPage: boolean;
      hasPreviousPage: boolean;
    }>>(
      `/v1/tracking/trips/${TEST_TRIP_ID}/trail?from=2026-06-03T09:00:00.000Z&to=2026-06-03T11:00:00.000Z&page=1&pageSize=20`,
      token,
    );

    expect(response.status).toBe(200);
    expect(prismaFindMany).toHaveBeenCalledWith(
      expect.objectContaining({
        orderBy: { recordedAt: 'asc' },
      }),
    );
    expect(response.body.data?.items.map((item) => item.recordedAt)).toEqual([
      '2026-06-03T17:00:00.000+07:00',
      '2026-06-03T17:05:00.000+07:00',
    ]);
    expect(response.body.data?.page).toBe(1);
    expect(response.body.data?.pageSize).toBe(20);
    expect(response.body.data?.totalItems).toBe(2);
    expect(response.body.data?.totalPages).toBe(1);
    expect(response.body.data?.hasNextPage).toBe(false);
    expect(response.body.data?.hasPreviousPage).toBe(false);
  });

  it('normalizes explicit offsets and rejects an offsetless trail timestamp with 422', async () => {
    const token = await signIdentityToken('PASSENGER', TEST_USER_ID);
    const accepted = await getJson<ApiEnvelope<unknown>>(
      `/v1/tracking/trips/${TEST_TRIP_ID}/trail?from=2026-06-03T16:00:00%2B07:00&to=2026-06-03T18:00:00%2B07:00`,
      token,
    );
    expect(accepted.status).toBe(200);
    expect(prismaFindMany).toHaveBeenLastCalledWith(expect.objectContaining({
      where: expect.objectContaining({
        recordedAt: {
          gte: new Date('2026-06-03T09:00:00.000Z'),
          lte: new Date('2026-06-03T11:00:00.000Z'),
        },
      }),
    }));

    const rejected = await getJson<ApiEnvelope<unknown>>(
      `/v1/tracking/trips/${TEST_TRIP_ID}/trail?from=2026-06-03T16:00:00&to=2026-06-03T18:00:00`,
      token,
    );
    expect(rejected.status).toBe(422);
    expect(rejected.body.error?.code).toBe('VALIDATION_ERROR');
  });

  it('returns cached ETA from Redis', async () => {
    const token = await signIdentityToken('PASSENGER', TEST_USER_ID);
    const response = await getJson<ApiEnvelope<{ eta: { etaMinutes: number } }>>(
      `/v1/tracking/trips/${TEST_TRIP_ID}/eta?stopId=${TEST_STOP_ID}`,
      token,
    );

    expect(response.status).toBe(200);
    expect(response.body.data?.eta).toEqual(
      expect.objectContaining({
        etaMinutes: 12,
        delayed: null,
        delayStatus: 'UNKNOWN',
        delayMinutes: null,
      }),
    );
  });

  it('does not apply a delay state belonging to a different ETA stop', async () => {
    delayStatePayload = JSON.stringify({
      tripId: TEST_TRIP_ID,
      stopId: '77777777-7777-4777-8777-777777777777',
      delayStatus: 'DELAYED',
      delayMinutes: 45,
      evaluatedAt: '2026-06-03T10:00:00.000Z',
    });
    const token = await signIdentityToken('PASSENGER', TEST_USER_ID);
    const response = await getJson<ApiEnvelope<{
      eta: { delayed: boolean | null; delayStatus: string; delayMinutes: number | null };
    }>>(
      `/v1/tracking/trips/${TEST_TRIP_ID}/eta?stopId=${TEST_STOP_ID}`,
      token,
    );

    expect(response.status).toBe(200);
    expect(response.body.data?.eta).toEqual(expect.objectContaining({
      delayed: null,
      delayStatus: 'UNKNOWN',
      delayMinutes: null,
    }));
    delayStatePayload = null;
  });

  it('rejects a legacy ETA cache with mismatched trip or stop identity', async () => {
    etaPayload = {
      tripId: TEST_TRIP_ID,
      stopId: '77777777-7777-4777-8777-777777777777',
      etaMinutes: 12,
      estimatedArrivalTime: '2026-06-03T10:13:00.000Z',
      distanceMeters: 8500,
      updatedAt: '2026-06-03T10:01:00.000Z',
    };
    const token = await signIdentityToken('PASSENGER', TEST_USER_ID);
    const response = await getJson<ApiEnvelope<{ eta: unknown | null }>>(
      `/v1/tracking/trips/${TEST_TRIP_ID}/eta?stopId=${TEST_STOP_ID}`,
      token,
    );

    expect(response.status).toBe(200);
    expect(response.body.data?.eta).toBeNull();
    etaPayload = {
      tripId: TEST_TRIP_ID,
      stopId: TEST_STOP_ID,
      etaMinutes: 12,
      estimatedArrivalTime: '2026-06-03T10:13:00.000Z',
      distanceMeters: 8500,
      updatedAt: '2026-06-03T10:01:00.000Z',
    };
  });

  it('treats a negative delay state as UNKNOWN on REST', async () => {
    delayStatePayload = JSON.stringify({
      tripId: TEST_TRIP_ID,
      stopId: TEST_STOP_ID,
      delayStatus: 'DELAYED',
      delayMinutes: -1,
      evaluatedAt: '2026-06-03T10:00:00.000Z',
    });
    const token = await signIdentityToken('PASSENGER', TEST_USER_ID);
    const response = await getJson<ApiEnvelope<{
      eta: { delayed: boolean | null; delayStatus: string; delayMinutes: number | null };
    }>>(
      `/v1/tracking/trips/${TEST_TRIP_ID}/eta?stopId=${TEST_STOP_ID}`,
      token,
    );

    expect(response.status).toBe(200);
    expect(response.body.data?.eta).toEqual(expect.objectContaining({
      delayed: null,
      delayStatus: 'UNKNOWN',
      delayMinutes: null,
    }));
    delayStatePayload = null;
  });

  it('returns 400 envelope when ETA stopId is invalid', async () => {
    const token = await signIdentityToken('PASSENGER', TEST_USER_ID);
    const response = await getJson<ApiEnvelope<unknown>>(
      `/v1/tracking/trips/${TEST_TRIP_ID}/eta?stopId=bad-stop-id`,
      token,
    );

    expect(response.status).toBe(400);
    expect(response.body.success).toBe(false);
  });

  async function signIdentityToken(role: string, userId: string): Promise<string> {
    return new SignJWT({
      role,
      email: 'tracking-rest-test@vietride.local',
    })
      .setProtectedHeader({ alg: 'RS256', typ: 'JWT', kid: 'tracking-rest-e2e-key' })
      .setSubject(userId)
      .setIssuer(IDENTITY_ISSUER)
      .setAudience(IDENTITY_AUDIENCE)
      .setIssuedAt()
      .setExpirationTime('15m')
      .sign(privateKey);
  }

  async function getJson<TBody>(
    path: string,
    token?: string,
  ): Promise<{ status: number; body: TBody }> {
    const response = await fetch(`http://127.0.0.1:${port}${path}`, {
      headers: token ? { Authorization: `Bearer ${token}` } : {},
    });
    return {
      status: response.status,
      body: (await response.json()) as TBody,
    };
  }
});

class E2eTrackingAuthorizationAdapter implements TrackingAuthorizationAdapter {
  async authorizeTripTracking(
    user: TrackingUser,
    tripId: string,
  ): Promise<TrackingAuthorizationResult> {
    void tripId;
    if (user.userId === UNAUTHORIZED_USER_ID) {
      return { allowed: false, error: 'ACCESS_DENIED' };
    }

    return { allowed: true, scope: 'BOOKING_OWNER' };
  }
}

function createTestEnv(publicKeyPem: string): Env {
  return {
    NODE_ENV: 'test',
    PORT: 3001,
    GATEWAY_URL: 'http://gateway:3000',
    INTERNAL_JWT_SECRET: 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa',
    INTERNAL_JWT_TTL_SEC: 120,
    JWT_PUBLIC_KEY_URL: 'http://identity.test/v1/.well-known/jwks.json',
    JWT_ISSUER: IDENTITY_ISSUER,
    JWT_AUDIENCE: IDENTITY_AUDIENCE,
    REDIS_URL: 'redis://localhost:6379',
    REDIS_HOST: 'localhost',
    REDIS_PORT: 6379,
    RABBITMQ_URL: 'amqp://guest:guest@localhost:5672',
    RABBITMQ_EXCHANGE: 'vietride.events',
    DATABASE_URL: 'postgresql://postgres:postgres@localhost:5432/vietride_tracking',
    LOG_LEVEL: 'info',
    USER_JWT_PUBLIC_KEY: publicKeyPem,
    TRIP_SERVICE_BASE_URL: 'http://trip.test',
    BOOKING_SERVICE_BASE_URL: 'http://booking.test',
    PARCEL_SERVICE_BASE_URL: 'http://parcel.test',
    TRIP_TRACKING_AUTH_PATH: '/internal/v1/trips/:tripId/tracking-authorization',
    BOOKING_TRACKING_AUTH_PATH: '/internal/v1/trips/:tripId/tracking-authorization/bookings',
    PARCEL_TRACKING_AUTH_PATH: '/internal/v1/trips/:tripId/tracking-authorization/parcels',
    TRACKING_AUTH_HTTP_TIMEOUT_MS: 2_000,
    TRACKING_CORS_ORIGIN: '*',
    TRACKING_SWAGGER_ENABLED: true,
    TRACKING_GPS_FLUSH_ENABLED: false,
    TRACKING_GPS_FLUSH_INTERVAL_MS: 300_000,
    TRACKING_TRIP_DELAY_ENABLED: false,
    TRACKING_TRIP_DELAY_INTERVAL_MS: 300_000,
    TRACKING_OUTBOX_PUBLISH_ENABLED: false,
    TRACKING_OUTBOX_PUBLISH_INTERVAL_MS: 5_000,
    TRACKING_OUTBOX_PUBLISH_BATCH_SIZE: 25,
    TRIP_ROUTE_STOPS_PATH: '/internal/v1/trips/:tripId/route-stops',
    TRIP_ROUTE_GEOMETRY_PATH: '/internal/v1/trips/:tripId/route-geometry',
    BOOKING_PICKUP_BOOKINGS_PATH: '/internal/v1/trips/:tripId/stops/:stopId/pickup-bookings',
    TRACKING_DATA_PROVIDER_TIMEOUT_MS: 2_000,
    TRACKING_ROUTE_STOPS_CACHE_TTL_SECONDS: 300,
    TRACKING_ROUTE_GEOMETRY_CACHE_TTL_SECONDS: 600,
    TRACKING_SHARE_TOKEN_SECRET: 'phase13-test-share-token-secret-32-bytes',
    TRACKING_SHARE_PAGE_URL: 'http://localhost:5173/trip-sharing',
    TRACKING_SHARE_TOKEN_TTL_SECONDS: 86_400,
    TRACKING_SHARE_CONTEXT_RATE_LIMIT_PER_MIN: 60,
    TRACKING_SHARE_SOCKET_RATE_LIMIT_PER_MIN: 20,
    TRACKING_SHARE_SOCKET_REVALIDATE_SECONDS: 60,
    GOOGLE_ROUTES_ENABLED: false,
    GOOGLE_ROUTES_API_KEY: '',
    GOOGLE_ROUTES_BASE_URL: 'https://routes.googleapis.com',
    TRACKING_GOOGLE_ROUTES_TIMEOUT_MS: 1_500,
    TRACKING_ETA_MIN_INTERVAL_SECONDS: 60,
    TRACKING_ETA_CACHE_TTL_SECONDS: 60,
    TRACKING_ETA_FAILURE_COOLDOWN_SECONDS: 300,
  };
}

function readListeningPort(app: INestApplication): number {
  const server = app.getHttpServer() as {
    address(): string | { port: number } | null;
  };
  const address = server.address();
  if (typeof address === 'object' && address !== null) {
    return address.port;
  }

  throw new Error('TRACKING_REST_E2E_PORT_UNAVAILABLE');
}
