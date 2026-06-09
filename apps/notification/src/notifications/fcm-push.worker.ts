import { Inject, Injectable, OnModuleDestroy, OnModuleInit } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import { Job, Worker } from 'bullmq';
import IORedis from 'ioredis';
import pino from 'pino';
import { DEVICE_TOKEN_PROVIDER, ENV_TOKEN, FCM_PUSH_PROVIDER } from '../app/tokens';
import type { Env } from '../config/env.schema';
import { NotificationDeliveryStatus, type Notification } from '../generated/notification-prisma-client';
import {
  FCM_PUSH_ATTEMPTS,
  FCM_PUSH_BACKOFF_DELAYS_MS,
  FCM_PUSH_QUEUE_NAME,
  FCM_TOKEN_BLACKLIST_PREFIX,
  FCM_TOKEN_BLACKLIST_TTL_SECONDS,
} from './fcm-push.constants';
import type {
  DeviceTokenProvider,
  DeviceTokenSnapshot,
  FcmPushJobData,
  FcmPushProvider,
} from './fcm-push.types';
import { NotificationsRepository } from './notifications.repository';

const LAST_ERROR_MAX_LENGTH = 1_000;
const LAST_FCM_PUSH_BACKOFF_DELAY_MS = 300_000;

@Injectable()
export class FcmPushWorker implements OnModuleInit, OnModuleDestroy {
  private readonly logger = pino({ name: FcmPushWorker.name });
  private connection: IORedis | null = null;
  private worker: Worker<FcmPushJobData> | null = null;

  constructor(
    @Inject(ENV_TOKEN) private readonly env: Env,
    @Inject(DEVICE_TOKEN_PROVIDER) private readonly deviceTokenProvider: DeviceTokenProvider,
    @Inject(FCM_PUSH_PROVIDER) private readonly fcmPushProvider: FcmPushProvider,
    private readonly notificationsRepository: NotificationsRepository,
    private readonly redis: RedisService,
  ) {}

  onModuleInit(): void {
    this.connection = new IORedis(this.env.REDIS_URL, { maxRetriesPerRequest: null });
    this.worker = new Worker<FcmPushJobData>(
      FCM_PUSH_QUEUE_NAME,
      (job) => this.process(job),
      {
        connection: this.connection,
        settings: {
          backoffStrategy: (attemptsMade) =>
            FCM_PUSH_BACKOFF_DELAYS_MS[attemptsMade - 1] ??
            LAST_FCM_PUSH_BACKOFF_DELAY_MS,
        },
      },
    );
  }

  async onModuleDestroy(): Promise<void> {
    if (this.worker) {
      await this.worker.close();
    }
    if (this.connection) {
      await this.connection.quit();
    }
  }

  async process(job: Job<FcmPushJobData>): Promise<void> {
    const notification = await this.notificationsRepository.findById(job.data.notificationId);
    if (!notification) {
      this.logger.warn({ notificationId: job.data.notificationId }, 'Skipping FCM push for missing notification');
      return;
    }

    const deliveries = await this.resolveDeliverySnapshot(notification.id, job.data.userId);
    const currentAttempt = job.attemptsMade + 1;
    let hasRetryableFailure = false;

    for (const delivery of deliveries) {
      if (
        delivery.status === NotificationDeliveryStatus.SENT ||
        delivery.status === NotificationDeliveryStatus.FAILED
      ) {
        continue;
      }

      const result = await this.sendDelivery(notification, delivery.fcmToken);
      if (result === 'sent') {
        await this.notificationsRepository.markDeliverySent(delivery.id);
        continue;
      }

      if (result === 'invalid-token') {
        await this.blacklistToken(delivery.fcmToken);
        await this.notificationsRepository.markDeliveryFailed(delivery.id, currentAttempt, 'FCM_TOKEN_INVALID');
        continue;
      }

      hasRetryableFailure = true;
      if (currentAttempt >= FCM_PUSH_ATTEMPTS) {
        await this.notificationsRepository.markDeliveryFailed(delivery.id, currentAttempt, result);
      } else {
        await this.notificationsRepository.markDeliveryRetrying(delivery.id, currentAttempt, result);
      }
    }

    if (hasRetryableFailure && currentAttempt < FCM_PUSH_ATTEMPTS) {
      throw new Error('FCM_PUSH_RETRYABLE_FAILURE');
    }
  }

  private async resolveDeliverySnapshot(notificationId: string, userId: string) {
    const existingDeliveries = await this.notificationsRepository.listDeliveriesByNotificationId(notificationId);
    if (existingDeliveries.length > 0) {
      return existingDeliveries;
    }

    const deviceTokens = await this.deviceTokenProvider.listActiveDeviceTokens(userId);
    const deliverableTokens = await this.filterBlacklistedTokens(deviceTokens);
    const deliveries = [];

    for (const deviceToken of deliverableTokens) {
      deliveries.push(await this.notificationsRepository.createDelivery(notificationId, deviceToken));
    }

    return deliveries;
  }

  private async filterBlacklistedTokens(deviceTokens: DeviceTokenSnapshot[]): Promise<DeviceTokenSnapshot[]> {
    const deliverableTokens = [];
    for (const deviceToken of deviceTokens) {
      const isBlacklisted = await this.redis.get(`${FCM_TOKEN_BLACKLIST_PREFIX}${deviceToken.fcmToken}`);
      if (!isBlacklisted) {
        deliverableTokens.push(deviceToken);
      }
    }

    return deliverableTokens;
  }

  private async sendDelivery(
    notification: Notification,
    token: string,
  ): Promise<'sent' | 'invalid-token' | string> {
    try {
      const result = await this.fcmPushProvider.send({
        token,
        title: notification.title,
        body: notification.body,
        data: this.toFcmData(notification),
      });

      return result.invalidToken ? 'invalid-token' : 'sent';
    } catch (error) {
      return this.normalizeError(error);
    }
  }

  private async blacklistToken(token: string): Promise<void> {
    await this.redis.set(`${FCM_TOKEN_BLACKLIST_PREFIX}${token}`, '1', FCM_TOKEN_BLACKLIST_TTL_SECONDS);
  }

  private toFcmData(notification: Notification): Record<string, string> {
    const data: Record<string, string> = {
      notificationId: notification.id,
      type: notification.type,
    };

    if (notification.data && typeof notification.data === 'object' && !Array.isArray(notification.data)) {
      for (const [key, value] of Object.entries(notification.data)) {
        if (value === null || value === undefined) continue;
        data[key] = typeof value === 'string' ? value : JSON.stringify(value);
      }
    }

    return data;
  }

  private normalizeError(error: unknown): string {
    const message = error instanceof Error ? error.message : String(error);

    return message.slice(0, LAST_ERROR_MAX_LENGTH);
  }
}
