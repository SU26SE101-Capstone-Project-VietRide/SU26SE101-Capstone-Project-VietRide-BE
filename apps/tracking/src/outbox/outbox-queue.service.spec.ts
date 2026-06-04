import type IORedis from 'ioredis';
import type { Env } from '../config/env.schema';
import {
  OUTBOX_JOB_NAME,
  OUTBOX_QUEUE_NAME,
  OUTBOX_SCHEDULER_ID,
  OUTBOX_WORKER_CONCURRENCY,
} from './outbox.constants';
import { OutboxPublisherService } from './outbox-publisher.service';
import { OutboxQueueService } from './outbox-queue.service';

describe('OutboxQueueService', () => {
  it('does not create queue infrastructure when disabled', async () => {
    const service = new TestOutboxQueueService(createEnv({ TRACKING_OUTBOX_PUBLISH_ENABLED: false }));

    await service.onModuleInit();

    expect(service.createConnectionCalled).toBe(false);
  });

  it('schedules repeat publish job when enabled', async () => {
    const service = new TestOutboxQueueService(
      createEnv({
        TRACKING_OUTBOX_PUBLISH_ENABLED: true,
        TRACKING_OUTBOX_PUBLISH_INTERVAL_MS: 7_000,
        TRACKING_OUTBOX_PUBLISH_BATCH_SIZE: 12,
      }),
    );

    await service.onModuleInit();

    expect(service.queueName).toBe(OUTBOX_QUEUE_NAME);
    expect(service.workerName).toBe(OUTBOX_QUEUE_NAME);
    expect(service.workerConcurrency).toBe(OUTBOX_WORKER_CONCURRENCY);
    expect(service.mockQueue.upsertJobScheduler).toHaveBeenCalledWith(
      OUTBOX_SCHEDULER_ID,
      { every: 7_000 },
      {
        name: OUTBOX_JOB_NAME,
        data: {},
        opts: {
          removeOnComplete: true,
          removeOnFail: 100,
        },
      },
    );

    await expect(service.processor()).resolves.toBe(3);
    expect(service.mockPublisherService.publishPendingOnce).toHaveBeenCalledWith(12);
  });
});

class TestOutboxQueueService extends OutboxQueueService {
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
  readonly mockPublisherService = {
    publishPendingOnce: jest.fn(async (limit: number) => {
      void limit;
      return 3;
    }),
  };
  createConnectionCalled = false;
  queueName?: string;
  workerName?: string;
  workerConcurrency?: number;
  processor: () => Promise<number> = async () => 0;

  constructor(env: Env) {
    super(env, { publishPendingOnce: jest.fn(async () => 0) } as unknown as OutboxPublisherService);
  }

  protected override createConnection(): IORedis {
    this.createConnectionCalled = true;
    return this.mockConnection as unknown as IORedis;
  }

  protected override createQueue(connection: IORedis): never {
    expect(connection).toBe(this.mockConnection);
    this.queueName = OUTBOX_QUEUE_NAME;
    return this.mockQueue as never;
  }

  protected override createWorker(connection: IORedis): never {
    expect(connection).toBe(this.mockConnection);
    this.workerName = OUTBOX_QUEUE_NAME;
    this.workerConcurrency = OUTBOX_WORKER_CONCURRENCY;
    this.processor = () => this.mockPublisherService.publishPendingOnce(this.env.TRACKING_OUTBOX_PUBLISH_BATCH_SIZE);
    return this.mockWorker as never;
  }
}

function createEnv(overrides: Partial<Env>): Env {
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
    TRACKING_TRIP_DELAY_ENABLED: false,
    TRACKING_TRIP_DELAY_INTERVAL_MS: 300_000,
    TRACKING_OUTBOX_PUBLISH_ENABLED: false,
    TRACKING_OUTBOX_PUBLISH_INTERVAL_MS: 5_000,
    TRACKING_OUTBOX_PUBLISH_BATCH_SIZE: 25,
    ...overrides,
  };
}
