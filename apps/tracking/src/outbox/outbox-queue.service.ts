import { Inject, Injectable, Logger, OnModuleDestroy, OnModuleInit } from '@nestjs/common';
import { Queue, Worker } from 'bullmq';
import IORedis from 'ioredis';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import {
  OUTBOX_JOB_NAME,
  OUTBOX_QUEUE_NAME,
  OUTBOX_SCHEDULER_ID,
  OUTBOX_WORKER_CONCURRENCY,
} from './outbox.constants';
import { OutboxPublisherService } from './outbox-publisher.service';

type OutboxQueue = Queue<Record<string, never>, number, string>;
type OutboxWorker = Worker<Record<string, never>, number, string>;

@Injectable()
export class OutboxQueueService implements OnModuleInit, OnModuleDestroy {
  private readonly logger = new Logger(OutboxQueueService.name);
  private queue?: OutboxQueue;
  private worker?: OutboxWorker;
  private connection?: IORedis;

  constructor(
    @Inject(ENV_TOKEN) protected readonly env: Env,
    private readonly publisherService: OutboxPublisherService,
  ) {}

  async onModuleInit(): Promise<void> {
    if (!this.env.TRACKING_OUTBOX_PUBLISH_ENABLED) {
      this.logger.log('Outbox publisher is disabled');
      return;
    }

    this.connection = this.createConnection();
    this.queue = this.createQueue(this.connection);
    this.worker = this.createWorker(this.connection);
    this.worker.on('failed', (_job, error) => {
      this.logger.error(`Outbox publish job failed: ${error.message}`);
    });

    await this.queue.upsertJobScheduler(
      OUTBOX_SCHEDULER_ID,
      { every: this.env.TRACKING_OUTBOX_PUBLISH_INTERVAL_MS },
      {
        name: OUTBOX_JOB_NAME,
        data: {},
        opts: {
          removeOnComplete: true,
          removeOnFail: 100,
        },
      },
    );

    this.logger.log(`Outbox publisher scheduled every ${this.env.TRACKING_OUTBOX_PUBLISH_INTERVAL_MS}ms`);
  }

  async onModuleDestroy(): Promise<void> {
    await this.worker?.close();
    await this.queue?.close();
    await this.connection?.quit();
  }

  protected createConnection(): IORedis {
    return new IORedis(this.env.REDIS_URL, { maxRetriesPerRequest: null });
  }

  protected createQueue(connection: IORedis): OutboxQueue {
    return new Queue(OUTBOX_QUEUE_NAME, { connection });
  }

  protected createWorker(connection: IORedis): OutboxWorker {
    return new Worker(
      OUTBOX_QUEUE_NAME,
      async () => this.publisherService.publishPendingOnce(this.env.TRACKING_OUTBOX_PUBLISH_BATCH_SIZE),
      {
        connection,
        concurrency: OUTBOX_WORKER_CONCURRENCY,
      },
    );
  }
}
