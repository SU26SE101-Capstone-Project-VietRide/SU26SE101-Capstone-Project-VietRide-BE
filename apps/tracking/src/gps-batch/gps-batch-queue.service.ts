import { Inject, Injectable, Logger, OnModuleDestroy, OnModuleInit } from '@nestjs/common';
import { Queue, Worker } from 'bullmq';
import IORedis from 'ioredis';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import {
  GPS_BATCH_FLUSH_JOB_NAME,
  GPS_BATCH_QUEUE_NAME,
  GPS_BATCH_SCHEDULER_ID,
  GPS_BATCH_WORKER_CONCURRENCY,
} from './gps-batch.constants';
import { GpsBatchFlushService } from './gps-batch-flush.service';

type GpsBatchQueue = Queue<Record<string, never>, number, string>;
type GpsBatchWorker = Worker<Record<string, never>, number, string>;

@Injectable()
export class GpsBatchQueueService implements OnModuleInit, OnModuleDestroy {
  private readonly logger = new Logger(GpsBatchQueueService.name);
  private queue?: GpsBatchQueue;
  private worker?: GpsBatchWorker;
  private connection?: IORedis;

  constructor(
    @Inject(ENV_TOKEN) private readonly env: Env,
    private readonly flushService: GpsBatchFlushService,
  ) {}

  async onModuleInit(): Promise<void> {
    if (!this.env.TRACKING_GPS_FLUSH_ENABLED) {
      this.logger.log('GPS batch flush is disabled');
      return;
    }

    this.connection = this.createConnection();
    this.queue = this.createQueue(this.connection);
    this.worker = this.createWorker(this.connection);
    this.worker.on('failed', (_job, error) => {
      this.logger.error(`GPS batch flush job failed: ${error.message}`);
    });

    await this.queue.upsertJobScheduler(
      GPS_BATCH_SCHEDULER_ID,
      { every: this.env.TRACKING_GPS_FLUSH_INTERVAL_MS },
      {
        name: GPS_BATCH_FLUSH_JOB_NAME,
        data: {},
        opts: {
          removeOnComplete: true,
          removeOnFail: 100,
        },
      },
    );

    this.logger.log(`GPS batch flush scheduled every ${this.env.TRACKING_GPS_FLUSH_INTERVAL_MS}ms`);
  }

  async onModuleDestroy(): Promise<void> {
    await this.worker?.close();
    await this.queue?.close();
    await this.connection?.quit();
  }

  protected createConnection(): IORedis {
    return new IORedis(this.env.REDIS_URL, { maxRetriesPerRequest: null });
  }

  protected createQueue(connection: IORedis): GpsBatchQueue {
    return new Queue(GPS_BATCH_QUEUE_NAME, { connection });
  }

  protected createWorker(connection: IORedis): GpsBatchWorker {
    return new Worker(
      GPS_BATCH_QUEUE_NAME,
      async () => this.flushService.flushOnce(),
      {
        connection,
        concurrency: GPS_BATCH_WORKER_CONCURRENCY,
      },
    );
  }
}
