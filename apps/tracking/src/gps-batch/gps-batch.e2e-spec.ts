import { Global, Module, type Type } from '@nestjs/common';
import { Test } from '@nestjs/testing';
import { RedisService } from '@vietride/nest-redis';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import { trackingGpsBufferKey } from '../location/location.constants';
import { TrackingPrismaService } from '../prisma/tracking-prisma.service';
import { GpsBatchFlushService } from './gps-batch-flush.service';
import { GpsBatchModule } from './gps-batch.module';

const TEST_TRIP_ID = '11111111-1111-4111-8111-111111111111';

describe('GpsBatchModule (e2e)', () => {
  it('wires the disabled batch module and flushes through mocked Redis/Prisma', async () => {
    const redisClient = {
      smembers: jest.fn(async () => [TEST_TRIP_ID]),
      lrange: jest.fn(async () => [
        JSON.stringify({
          tripId: TEST_TRIP_ID,
          latitude: 10.762622,
          longitude: 106.660172,
          recordedAt: '2026-06-03T10:00:00.000Z',
        }),
      ]),
      del: jest.fn(async () => 1),
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
    expect(redisClient.del).toHaveBeenCalledWith(trackingGpsBufferKey(TEST_TRIP_ID));

    await moduleRef.close();
  });
});

function createTestEnv(): Env {
  return {
    NODE_ENV: 'test',
    PORT: 3001,
    GATEWAY_URL: 'http://gateway:3000',
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
    TRACKING_GPS_FLUSH_ENABLED: false,
    TRACKING_GPS_FLUSH_INTERVAL_MS: 300_000,
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
