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
        removeOnComplete: true,
        removeOnFail: { age: 7 * 24 * 60 * 60, count: 1000 },
      },
    });
  }

  async enqueue(data: EmailSendJobData): Promise<void> {
    await this.queue.add(EMAIL_SEND_JOB_NAME, data, {
      jobId: data.emailDeliveryId,
    });
  }

  async onModuleDestroy(): Promise<void> {
    await this.queue.close();
    await this.connection.quit();
  }
}
