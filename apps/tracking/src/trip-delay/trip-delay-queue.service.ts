import { Inject, Injectable, Logger, OnModuleDestroy, OnModuleInit } from '@nestjs/common';
import { Queue, Worker } from 'bullmq';
import IORedis from 'ioredis';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import {
  TRIP_DELAY_JOB_NAME,
  TRIP_DELAY_QUEUE_NAME,
  TRIP_DELAY_SCHEDULER_ID,
  TRIP_DELAY_WORKER_CONCURRENCY,
} from './trip-delay.constants';
import { TripDelayService } from './trip-delay.service';

type TripDelayQueue = Queue<Record<string, never>, number, string>;
type TripDelayWorker = Worker<Record<string, never>, number, string>;

@Injectable()
export class TripDelayQueueService implements OnModuleInit, OnModuleDestroy {
  private readonly logger = new Logger(TripDelayQueueService.name);
  private queue?: TripDelayQueue;
  private worker?: TripDelayWorker;
  private connection?: IORedis;

  constructor(
    @Inject(ENV_TOKEN) private readonly env: Env,
    private readonly tripDelayService: TripDelayService,
  ) {}

  async onModuleInit(): Promise<void> {
    if (!this.env.TRACKING_TRIP_DELAY_ENABLED) {
      this.logger.log('Trip delayed detection is disabled');
      return;
    }

    this.connection = this.createConnection();
    this.queue = this.createQueue(this.connection);
    this.worker = this.createWorker(this.connection);
    this.worker.on('failed', (_job, error) => {
      this.logger.error(`Trip delayed detection job failed: ${error.message}`);
    });

    await this.queue.upsertJobScheduler(
      TRIP_DELAY_SCHEDULER_ID,
      { every: this.env.TRACKING_TRIP_DELAY_INTERVAL_MS },
      {
        name: TRIP_DELAY_JOB_NAME,
        data: {},
        opts: {
          removeOnComplete: true,
          removeOnFail: 100,
        },
      },
    );

    this.logger.log(`Trip delayed detection scheduled every ${this.env.TRACKING_TRIP_DELAY_INTERVAL_MS}ms`);
  }

  async onModuleDestroy(): Promise<void> {
    await this.worker?.close();
    await this.queue?.close();
    await this.connection?.quit();
  }

  protected createConnection(): IORedis {
    return new IORedis(this.env.REDIS_URL, { maxRetriesPerRequest: null });
  }

  protected createQueue(connection: IORedis): TripDelayQueue {
    return new Queue(TRIP_DELAY_QUEUE_NAME, { connection });
  }

  protected createWorker(connection: IORedis): TripDelayWorker {
    return new Worker(
      TRIP_DELAY_QUEUE_NAME,
      async () => this.tripDelayService.detectDelayedTrips(),
      {
        connection,
        concurrency: TRIP_DELAY_WORKER_CONCURRENCY,
      },
    );
  }
}
