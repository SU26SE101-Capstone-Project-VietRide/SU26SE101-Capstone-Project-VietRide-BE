import { Inject, Injectable, OnModuleDestroy } from '@nestjs/common';
import { Queue } from 'bullmq';
import IORedis from 'ioredis';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import {
  EMAIL_SEND_ATTEMPTS,
  EMAIL_SEND_JOB_NAME,
  EMAIL_SEND_QUEUE_NAME,
} from './email-send.constants';
import { BULLMQ_QUEUE_PREFIX } from './fcm-push.constants';
import type { EmailSendJobData } from './email-send.types';

@Injectable()
export class EmailSendQueue implements OnModuleDestroy {
  private readonly connection: IORedis;
  private readonly queue: Queue<EmailSendJobData>;

  constructor(@Inject(ENV_TOKEN) private readonly env: Env) {
    this.connection = new IORedis(env.REDIS_URL, { maxRetriesPerRequest: null });
    this.queue = new Queue<EmailSendJobData>(EMAIL_SEND_QUEUE_NAME, {
      connection: this.connection,
      prefix: BULLMQ_QUEUE_PREFIX,
      defaultJobOptions: {
        attempts: EMAIL_SEND_ATTEMPTS,
        removeOnComplete: {
          age: 86_400,
          count: 10_000,
        },
        backoff: {
          type: 'custom',
          delay: 0,
        },
        removeOnFail: { age: 7 * 24 * 60 * 60, count: 1000 },
      },
    });
  }

  async enqueue(data: EmailSendJobData): Promise<void> {
    const existingJob = await this.queue.getJob(data.emailDeliveryId);
    if (!existingJob) {
      await this.add(data);
      return;
    }

    await this.ensureRunnable(existingJob);
  }

  async retryRetained(emailDeliveryId: string): Promise<boolean> {
    const existingJob = await this.queue.getJob(emailDeliveryId);
    if (!existingJob) return false;

    await this.ensureRunnable(existingJob);
    return true;
  }

  private async ensureRunnable(
    job: NonNullable<Awaited<ReturnType<Queue<EmailSendJobData>['getJob']>>>,
  ): Promise<void> {
    const state = await job.getState();
    switch (state) {
      case 'waiting':
      case 'delayed':
      case 'active':
      case 'prioritized':
      case 'waiting-children':
        return;
      case 'failed':
        await job.retry('failed', { resetAttemptsMade: true });
        return;
      case 'completed':
        await job.retry('completed', { resetAttemptsMade: true });
        return;
      default:
        throw new Error(`NOTIFICATION_QUEUE_JOB_STATE_UNKNOWN:${state}`);
    }
  }

  private async add(data: EmailSendJobData): Promise<void> {
    await this.queue.add(EMAIL_SEND_JOB_NAME, data, { jobId: data.emailDeliveryId });
  }

  async onModuleDestroy(): Promise<void> {
    await this.queue.close();
    await this.connection.quit();
  }
}
