import { INestApplication } from '@nestjs/common';
import { APP_FILTER, APP_INTERCEPTOR } from '@nestjs/core';
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
import { TrackingPrismaService } from '../prisma/tracking-prisma.service';
import { TrackingDataController } from './tracking-data.controller';
import { TrackingDataRepository } from './tracking-data.repository';
import { TrackingDataService } from './tracking-data.service';

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

  beforeAll(async () => {
    const generated = await generateKeyPair('RS256');
    privateKey = generated.privateKey;
    const publicKeyPem = await exportSPKI(generated.publicKey);

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
        return JSON.stringify({
          tripId: TEST_TRIP_ID,
          stopId: TEST_STOP_ID,
          etaMinutes: 12,
          updatedAt: '2026-06-03T10:01:00.000Z',
        });
      }
      return null;
    });

    prismaFindMany = jest.fn(async (args: unknown) => {
      void args;
      return [
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
    });

    const moduleRef = await Test.createTestingModule({
      controllers: [TrackingDataController],
      providers: [
        TrackingDataService,
        TrackingDataRepository,
        { provide: ENV_TOKEN, useValue: createTestEnv(publicKeyPem) },
        { provide: TRACKING_JWT_VERIFIER, useClass: JoseUserJwtVerifier },
        { provide: TRACKING_AUTHORIZATION_ADAPTER, useClass: E2eTrackingAuthorizationAdapter },
        {
          provide: RedisService,
          useValue: { getClient: jest.fn(() => ({ get: redisGet })) },
        },
        {
          provide: TrackingPrismaService,
          useValue: { gpsTrail: { findMany: prismaFindMany } },
        },
        { provide: APP_FILTER, useValue: new ApiResponseExceptionFilter() },
        { provide: APP_INTERCEPTOR, useValue: new ApiResponseInterceptor() },
      ],
    }).compile();

    app = moduleRef.createNestApplication();
    app.setGlobalPrefix('api');
    await app.listen(0);
    port = readListeningPort(app);
  });

  afterAll(async () => {
    if (app) await app.close();
  });

  it('returns 401 envelope when auth is missing', async () => {
    const response = await getJson<ApiEnvelope<unknown>>(`/api/v1/tracking/trips/${TEST_TRIP_ID}/latest`);

    expect(response.status).toBe(401);
    expect(response.body.success).toBe(false);
    expect(response.body.error?.code).toBe('UNAUTHORIZED');
  });

  it('returns 401 envelope when token is invalid', async () => {
    const response = await getJson<ApiEnvelope<unknown>>(
      `/api/v1/tracking/trips/${TEST_TRIP_ID}/latest`,
      'not-a-jwt',
    );

    expect(response.status).toBe(401);
    expect(response.body.error?.code).toBe('UNAUTHORIZED');
  });

  it('returns 403 envelope when trip authorization denies access', async () => {
    const token = await signIdentityToken('PASSENGER', UNAUTHORIZED_USER_ID);
    const response = await getJson<ApiEnvelope<unknown>>(
      `/api/v1/tracking/trips/${TEST_TRIP_ID}/latest`,
      token,
    );

    expect(response.status).toBe(403);
    expect(response.body.error?.code).toBe('ACCESS_DENIED');
  });

  it('returns latest tracking data from Redis', async () => {
    const token = await signIdentityToken('PASSENGER', TEST_USER_ID);
    const response = await getJson<ApiEnvelope<{ latest: { tripId: string; latitude: number } }>>(
      `/api/v1/tracking/trips/${TEST_TRIP_ID}/latest`,
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

  it('returns latest null when Redis has no latest point', async () => {
    const token = await signIdentityToken('PASSENGER', TEST_USER_ID);
    const missingTripId = '77777777-7777-4777-8777-777777777777';
    const response = await getJson<ApiEnvelope<{ latest: null }>>(
      `/api/v1/tracking/trips/${missingTripId}/latest`,
      token,
    );

    expect(response.status).toBe(200);
    expect(response.body.data?.latest).toBeNull();
  });

  it('returns trail points ordered by recordedAt ascending', async () => {
    const token = await signIdentityToken('PASSENGER', TEST_USER_ID);
    const response = await getJson<ApiEnvelope<{ items: Array<{ recordedAt: string }> }>>(
      `/api/v1/tracking/trips/${TEST_TRIP_ID}/trail?from=2026-06-03T09:00:00.000Z&to=2026-06-03T11:00:00.000Z`,
      token,
    );

    expect(response.status).toBe(200);
    expect(prismaFindMany).toHaveBeenCalledWith(
      expect.objectContaining({
        orderBy: { recordedAt: 'asc' },
      }),
    );
    expect(response.body.data?.items.map((item) => item.recordedAt)).toEqual([
      '2026-06-03T10:00:00.000Z',
      '2026-06-03T10:05:00.000Z',
    ]);
  });

  it('returns cached ETA from Redis', async () => {
    const token = await signIdentityToken('PASSENGER', TEST_USER_ID);
    const response = await getJson<ApiEnvelope<{ eta: { etaMinutes: number } }>>(
      `/api/v1/tracking/trips/${TEST_TRIP_ID}/eta?stopId=${TEST_STOP_ID}`,
      token,
    );

    expect(response.status).toBe(200);
    expect(response.body.data?.eta).toEqual(
      expect.objectContaining({
        etaMinutes: 12,
      }),
    );
  });

  it('returns 400 envelope when ETA stopId is invalid', async () => {
    const token = await signIdentityToken('PASSENGER', TEST_USER_ID);
    const response = await getJson<ApiEnvelope<unknown>>(
      `/api/v1/tracking/trips/${TEST_TRIP_ID}/eta?stopId=bad-stop-id`,
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
    TRIP_TRACKING_AUTH_PATH: '/internal/trips/:tripId/tracking-authorization',
    BOOKING_TRACKING_AUTH_PATH: '/internal/trips/:tripId/tracking-authorization/bookings',
    PARCEL_TRACKING_AUTH_PATH: '/internal/trips/:tripId/tracking-authorization/parcels',
    TRACKING_AUTH_HTTP_TIMEOUT_MS: 2_000,
    TRACKING_GPS_FLUSH_ENABLED: false,
    TRACKING_GPS_FLUSH_INTERVAL_MS: 300_000,
    TRACKING_TRIP_DELAY_ENABLED: false,
    TRACKING_TRIP_DELAY_INTERVAL_MS: 300_000,
    TRACKING_OUTBOX_PUBLISH_ENABLED: false,
    TRACKING_OUTBOX_PUBLISH_INTERVAL_MS: 5_000,
    TRACKING_OUTBOX_PUBLISH_BATCH_SIZE: 25,
  };
}

function readListeningPort(app: INestApplication): number {
  const address = app.getHttpServer().address();
  if (typeof address === 'object' && address !== null) {
    return address.port;
  }

  throw new Error('TRACKING_REST_E2E_PORT_UNAVAILABLE');
}
