import { Inject, Injectable, OnModuleDestroy } from '@nestjs/common';
import { Queue } from 'bullmq';
import IORedis from 'ioredis';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import {
  BULLMQ_QUEUE_PREFIX,
  FCM_PUSH_ATTEMPTS,
  FCM_PUSH_JOB_NAME,
  FCM_PUSH_QUEUE_NAME,
} from './fcm-push.constants';
import type { FcmPushJobData } from './fcm-push.types';

@Injectable()
export class FcmPushQueue implements OnModuleDestroy {
  private readonly connection: IORedis;
  private readonly queue: Queue<FcmPushJobData>;

  constructor(@Inject(ENV_TOKEN) private readonly env: Env) {
    this.connection = new IORedis(env.REDIS_URL, { maxRetriesPerRequest: null });
    this.queue = new Queue<FcmPushJobData>(FCM_PUSH_QUEUE_NAME, {
      connection: this.connection,
      prefix: BULLMQ_QUEUE_PREFIX,
      defaultJobOptions: {
        attempts: FCM_PUSH_ATTEMPTS,
        removeOnComplete: true,
        removeOnFail: false,
      },
    });
  }

  async enqueue(data: FcmPushJobData): Promise<void> {
    await this.queue.add(FCM_PUSH_JOB_NAME, data, {
      jobId: data.notificationId,
    });
  }

  async onModuleDestroy(): Promise<void> {
    await this.queue.close();
    await this.connection.quit();
  }
}
