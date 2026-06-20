import { Global, Module, type Type } from '@nestjs/common';
import { Test } from '@nestjs/testing';
import { RabbitMqPublisher } from '@vietride/nest-rabbitmq';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import { TrackingPrismaService } from '../prisma/tracking-prisma.service';
import { TRACKING_TRIP_DELAYED_ROUTING_KEY, TRIP_DELAYED_EVENT_TYPE } from './outbox.constants';
import { OutboxModule } from './outbox.module';
import { OutboxPublisherService } from './outbox-publisher.service';

const EVENT_ID = '11111111-1111-4111-8111-111111111111';

describe('OutboxModule (e2e)', () => {
  it('wires publisher through mocked Prisma/RabbitMQ and marks success', async () => {
    const event = {
      id: EVENT_ID,
      eventType: TRIP_DELAYED_EVENT_TYPE,
      payload: {
        tripId: EVENT_ID,
        stopId: '22222222-2222-4222-8222-222222222222',
      },
      status: 'PENDING',
      retryCount: 0,
      lastError: null,
      createdAt: new Date('2026-06-04T00:00:00.000Z'),
      updatedAt: new Date('2026-06-04T00:00:00.000Z'),
      publishedAt: null,
    };
    const prisma = {
      outboxEvent: {
        findMany: jest.fn(async (args: { where?: { status?: string } }) => {
          return args.where?.status === 'PENDING' ? [event] : [];
        }),
        updateMany: jest.fn(async (args: { where?: { id?: string } }) => ({
          count: args.where?.id === EVENT_ID ? 1 : 0,
        })),
        update: jest.fn(async () => event),
      },
    };
    const publisher = {
      publish: jest.fn(async () => undefined),
    };

    const moduleRef = await Test.createTestingModule({
      imports: [
        createOutboxTestGlobals(publisher),
        OutboxModule,
      ],
    })
      .overrideProvider(TrackingPrismaService)
      .useValue(prisma)
      .compile();

    const service = moduleRef.get(OutboxPublisherService);

    await expect(service.publishPendingOnce(25)).resolves.toBe(1);

    expect(prisma.outboxEvent.findMany).toHaveBeenNthCalledWith(1, {
      where: { status: 'PENDING' },
      orderBy: { createdAt: 'asc' },
      take: 25,
    });
    expect(publisher.publish).toHaveBeenCalledWith(
      TRACKING_TRIP_DELAYED_ROUTING_KEY,
      event.payload,
      {
        eventId: EVENT_ID,
        eventType: TRIP_DELAYED_EVENT_TYPE,
      },
    );
    expect(prisma.outboxEvent.update).toHaveBeenCalledWith({
      where: { id: EVENT_ID },
      data: {
        status: 'PUBLISHED',
        publishedAt: expect.any(Date),
        lastError: null,
      },
    });

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

function createOutboxTestGlobals(publisher: { publish: jest.MockedFunction<() => Promise<void>> }): Type<unknown> {
  @Global()
  @Module({
    providers: [
      { provide: ENV_TOKEN, useValue: createTestEnv() },
      { provide: RabbitMqPublisher, useValue: publisher },
    ],
    exports: [ENV_TOKEN, RabbitMqPublisher],
  })
  class OutboxTestGlobalsModule {}

  return OutboxTestGlobalsModule;
}
