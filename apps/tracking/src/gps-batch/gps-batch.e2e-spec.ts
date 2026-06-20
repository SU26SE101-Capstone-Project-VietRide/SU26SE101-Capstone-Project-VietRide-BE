import { Global, Module, type Type } from '@nestjs/common';
import { Test } from '@nestjs/testing';
import { RedisService } from '@vietride/nest-redis';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import { trackingGpsBufferKey, trackingGpsProcessingKey } from '../location/location.constants';
import { TrackingPrismaService } from '../prisma/tracking-prisma.service';
import { GpsBatchFlushService } from './gps-batch-flush.service';
import { GpsBatchModule } from './gps-batch.module';

const TEST_TRIP_ID = '11111111-1111-4111-8111-111111111111';

describe('GpsBatchModule (e2e)', () => {
  it('wires the disabled batch module and flushes through mocked Redis/Prisma', async () => {
    const gpsRows = [
      JSON.stringify({
        tripId: TEST_TRIP_ID,
        latitude: 10.762622,
        longitude: 106.660172,
        recordedAt: '2026-06-03T10:00:00.000Z',
      }),
    ];
    const redisClient = {
      smembers: jest.fn(async () => [TEST_TRIP_ID]),
      lrange: jest.fn(async () => gpsRows),
      del: jest.fn(async () => 1),
      eval: jest.fn(async () => gpsRows),
    };
    const prisma = {
      gpsTrail: {
        createMany: jest.fn(async () => ({ count: 1 })),
      },
    };

    const moduleRef = await Test.createTestingModule({
      imports: [
        createGpsBatchTestGlobals({
          redisService: { getClient: jest.fn(() => redisClient) },
          prisma,
        }),
        GpsBatchModule,
      ],
    }).compile();

    const flushService = moduleRef.get(GpsBatchFlushService);

    await expect(flushService.flushOnce()).resolves.toBe(1);

    expect(prisma.gpsTrail.createMany).toHaveBeenCalledWith({
      data: [
        {
          tripId: TEST_TRIP_ID,
          latitude: 10.762622,
          longitude: 106.660172,
          recordedAt: new Date('2026-06-03T10:00:00.000Z'),
        },
      ],
    });
    expect(redisClient.eval).toHaveBeenCalledWith(
      expect.any(String),
      2,
      trackingGpsBufferKey(TEST_TRIP_ID),
      trackingGpsProcessingKey(TEST_TRIP_ID),
    );
    expect(redisClient.del).toHaveBeenCalledWith(trackingGpsProcessingKey(TEST_TRIP_ID));

    await moduleRef.close();
  });
});

function createTestEnv(): Env {
  return {
    NODE_ENV: 'test',
    PORT: 3001,
    GATEWAY_URL: 'http://gateway:3000',
    INTERNAL_JWT_SECRET: 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa',
    INTERNAL_JWT_TTL_SEC: 120,
    JWT_PUBLIC_KEY_URL: 'http://identity.test/v1/.well-known/jwks.json',
    JWT_ISSUER: 'vietride-identity',
    JWT_AUDIENCE: 'vietride-api',
    REDIS_URL: 'redis://localhost:6379',
    REDIS_HOST: 'localhost',
    REDIS_PORT: 6379,
    RABBITMQ_URL: 'amqp://guest:guest@localhost:5672',
    RABBITMQ_EXCHANGE: 'vietride.events',
    DATABASE_URL: 'postgresql://postgres:postgres@localhost:5432/vietride_tracking',
    LOG_LEVEL: 'info',
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
  };
}

function createGpsBatchTestGlobals(deps: {
  redisService: { getClient: jest.MockedFunction<() => unknown> };
  prisma: { gpsTrail: { createMany: jest.MockedFunction<() => Promise<{ count: number }>> } };
}): Type<unknown> {
  @Global()
  @Module({
    providers: [
      { provide: ENV_TOKEN, useValue: createTestEnv() },
      { provide: RedisService, useValue: deps.redisService },
      { provide: TrackingPrismaService, useValue: deps.prisma },
    ],
    exports: [ENV_TOKEN, RedisService, TrackingPrismaService],
  })
  class GpsBatchTestGlobalsModule {}

  return GpsBatchTestGlobalsModule;
}
