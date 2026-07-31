import type IORedis from 'ioredis';
import type { Env } from '../config/env.schema';
import {
  GPS_BATCH_FLUSH_JOB_NAME,
  GPS_BATCH_QUEUE_NAME,
  GPS_BATCH_SCHEDULER_ID,
  GPS_BATCH_WORKER_CONCURRENCY,
} from './gps-batch.constants';
import { GpsBatchFlushService } from './gps-batch-flush.service';
import { GpsBatchQueueService } from './gps-batch-queue.service';

describe('GpsBatchQueueService', () => {
  it('does not create queue infrastructure when disabled', async () => {
    const service = new TestGpsBatchQueueService(createEnv({ TRACKING_GPS_FLUSH_ENABLED: false }));

    await service.onModuleInit();

    expect(service.createConnectionCalled).toBe(false);
  });

  it('schedules repeat flush job when enabled', async () => {
    const service = new TestGpsBatchQueueService(
      createEnv({
        TRACKING_GPS_FLUSH_ENABLED: true,
        TRACKING_GPS_FLUSH_INTERVAL_MS: 123_000,
      }),
    );

    await service.onModuleInit();

    expect(service.queueName).toBe(GPS_BATCH_QUEUE_NAME);
    expect(service.workerName).toBe(GPS_BATCH_QUEUE_NAME);
    expect(service.workerConcurrency).toBe(GPS_BATCH_WORKER_CONCURRENCY);
    expect(service.mockQueue.upsertJobScheduler).toHaveBeenCalledWith(
      GPS_BATCH_SCHEDULER_ID,
      { every: 123_000 },
      {
        name: GPS_BATCH_FLUSH_JOB_NAME,
        data: {},
        opts: {
          removeOnComplete: true,
          removeOnFail: 100,
        },
      },
    );

    await expect(service.processor()).resolves.toBe(7);
  });
});

class TestGpsBatchQueueService extends GpsBatchQueueService {
  readonly mockQueue = {
    upsertJobScheduler: jest.fn(async () => undefined),
    close: jest.fn(async () => undefined),
  };
  readonly mockWorker = {
    on: jest.fn(),
    close: jest.fn(async () => undefined),
  };
  readonly mockConnection = {
    quit: jest.fn(async () => undefined),
  };
  readonly mockFlushService = {
    flushOnce: jest.fn(async () => 7),
  };
  createConnectionCalled = false;
  queueName?: string;
  workerName?: string;
  workerConcurrency?: number;
  processor: () => Promise<number> = async () => 0;

  constructor(env: Env) {
    super(env, new GpsBatchFlushService({} as never, {} as never));
  }

  protected override createConnection(): IORedis {
    this.createConnectionCalled = true;
    return this.mockConnection as unknown as IORedis;
  }

  protected override createQueue(connection: IORedis): never {
    expect(connection).toBe(this.mockConnection);
    this.queueName = GPS_BATCH_QUEUE_NAME;
    return this.mockQueue as never;
  }

  protected override createWorker(connection: IORedis): never {
    expect(connection).toBe(this.mockConnection);
    this.workerName = GPS_BATCH_QUEUE_NAME;
    this.workerConcurrency = GPS_BATCH_WORKER_CONCURRENCY;
    this.processor = () => this.mockFlushService.flushOnce();
    return this.mockWorker as never;
  }
}

function createEnv(overrides: Partial<Env>): Env {
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
    TRIP_ROUTE_STOPS_PATH: '/internal/v1/trips/:tripId/route-stops',
    TRIP_ROUTE_GEOMETRY_PATH: '/internal/v1/trips/:tripId/route-geometry',
    BOOKING_PICKUP_BOOKINGS_PATH: '/internal/v1/trips/:tripId/stops/:stopId/pickup-bookings',
    TRACKING_AUTH_HTTP_TIMEOUT_MS: 2_000,
    TRACKING_DATA_PROVIDER_TIMEOUT_MS: 2_000,
    TRACKING_ROUTE_STOPS_CACHE_TTL_SECONDS: 300,
    TRACKING_ROUTE_GEOMETRY_CACHE_TTL_SECONDS: 600,
    GOOGLE_ROUTES_ENABLED: false,
    GOOGLE_ROUTES_API_KEY: '',
    GOOGLE_ROUTES_BASE_URL: 'https://routes.googleapis.com',
    TRACKING_GOOGLE_ROUTES_TIMEOUT_MS: 1_500,
    TRACKING_ETA_MIN_INTERVAL_SECONDS: 60,
    TRACKING_ETA_CACHE_TTL_SECONDS: 60,
    TRACKING_ETA_FAILURE_COOLDOWN_SECONDS: 300,
    TRACKING_CORS_ORIGIN: '*',
    TRACKING_SWAGGER_ENABLED: true,
    TRACKING_GPS_FLUSH_ENABLED: false,
    TRACKING_GPS_FLUSH_INTERVAL_MS: 300_000,
    TRACKING_TRIP_DELAY_ENABLED: false,
    TRACKING_TRIP_DELAY_INTERVAL_MS: 300_000,
    TRACKING_OUTBOX_PUBLISH_ENABLED: false,
    TRACKING_OUTBOX_PUBLISH_INTERVAL_MS: 5_000,
    TRACKING_OUTBOX_PUBLISH_BATCH_SIZE: 25,
    ...overrides,
  };
}
