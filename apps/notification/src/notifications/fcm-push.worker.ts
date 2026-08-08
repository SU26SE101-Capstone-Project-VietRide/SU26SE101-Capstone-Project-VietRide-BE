import { Inject, Injectable, OnModuleDestroy, OnModuleInit } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import { Job, Worker } from 'bullmq';
import IORedis from 'ioredis';
import { DEVICE_TOKEN_PROVIDER, ENV_TOKEN, FCM_PUSH_PROVIDER } from '../app/tokens';
import type { Env } from '../config/env.schema';
import {
  NotificationDeliveryStatus,
  type Notification,
} from '../generated/notification-prisma-client';
import {
  BULLMQ_QUEUE_PREFIX,
  FCM_PUSH_ATTEMPTS,
  FCM_PUSH_BACKOFF_DELAYS_MS,
  FCM_LAST_ERROR_MAX_LENGTH,
  FCM_PUSH_QUEUE_NAME,
  LAST_FCM_PUSH_BACKOFF_DELAY_MS,
  FCM_TOKEN_BLACKLIST_PREFIX,
  FCM_TOKEN_BLACKLIST_TTL_SECONDS,
} from './fcm-push.constants';
import type {
  DeviceTokenProvider,
  DeviceTokenSnapshot,
  FcmPushJobData,
  FcmPushProvider,
  FcmPushResult,
} from './fcm-push.types';
import { NotificationsRepository } from './notifications.repository';
import { createNotificationLogger } from './notification-logger';
import { normalizeSafeError } from './safe-error';

@Injectable()
export class FcmPushWorker implements OnModuleInit, OnModuleDestroy {
  private readonly logger = createNotificationLogger(FcmPushWorker.name);
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
    this.worker = new Worker<FcmPushJobData>(FCM_PUSH_QUEUE_NAME, (job) => this.process(job), {
      connection: this.connection,
      prefix: BULLMQ_QUEUE_PREFIX,
      settings: {
        backoffStrategy: (attemptsMade) =>
          FCM_PUSH_BACKOFF_DELAYS_MS[attemptsMade - 1] ?? LAST_FCM_PUSH_BACKOFF_DELAY_MS,
      },
    });
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
      this.logger.warn(
        { notificationId: job.data.notificationId },
        'Skipping FCM push for missing notification',
      );
      return;
    }

    const deliveries = await this.resolveDeliverySnapshot(notification.id, job.data.userId);
    const currentAttempt = job.attemptsMade + 1;
    let hasRetryableFailure = false;

    for (const delivery of deliveries) {
      if (
        delivery.status === NotificationDeliveryStatus.SENT ||
        delivery.status === NotificationDeliveryStatus.FAILED ||
        delivery.status === NotificationDeliveryStatus.VALIDATED
      ) {
        continue;
      }

      if (await this.isTokenBlacklisted(delivery.fcmToken)) {
        await this.deviceTokenProvider.deactivateDeviceToken(job.data.userId, delivery.fcmToken);
        await this.notificationsRepository.markDeliveryFailed(
          delivery.id,
          currentAttempt,
          'FCM_TOKEN_BLACKLISTED',
        );
        continue;
      }

      const result = await this.sendDelivery(notification, delivery.fcmToken);
      if (typeof result !== 'string' && !result.invalidToken) {
        if (result.dryRun) {
          await this.notificationsRepository.markDeliveryValidated(
            delivery.id,
            result.messageId ?? null,
          );
        } else {
          await this.notificationsRepository.markDeliverySent(
            delivery.id,
            result.messageId ?? null,
          );
        }
        continue;
      }

      if (typeof result !== 'string' && result.invalidToken) {
        await this.blacklistToken(delivery.fcmToken);
        await this.deviceTokenProvider.deactivateDeviceToken(job.data.userId, delivery.fcmToken);
        await this.notificationsRepository.markDeliveryFailed(
          delivery.id,
          currentAttempt,
          'FCM_TOKEN_INVALID',
        );
        continue;
      }

      hasRetryableFailure = true;
      const errorMessage = typeof result === 'string' ? result : 'FCM_PUSH_UNKNOWN_FAILURE';
      if (currentAttempt >= FCM_PUSH_ATTEMPTS) {
        await this.notificationsRepository.markDeliveryFailed(
          delivery.id,
          currentAttempt,
          errorMessage,
        );
      } else {
        await this.notificationsRepository.markDeliveryRetrying(
          delivery.id,
          currentAttempt,
          errorMessage,
        );
      }
    }

    if (hasRetryableFailure) {
      throw new Error('FCM_PUSH_RETRYABLE_FAILURE');
    }
  }

  private async resolveDeliverySnapshot(notificationId: string, userId: string) {
    const deviceTokens = await this.deviceTokenProvider.listActiveDeviceTokens(userId);
    const deliverableTokens = await this.filterBlacklistedTokens(deviceTokens);

    for (const deviceToken of deliverableTokens) {
      await this.notificationsRepository.createDelivery(notificationId, deviceToken);
    }

    return this.notificationsRepository.listDeliveriesByNotificationId(notificationId);
  }

  private async filterBlacklistedTokens(
    deviceTokens: DeviceTokenSnapshot[],
  ): Promise<DeviceTokenSnapshot[]> {
    const deliverableTokens = [];
    for (const deviceToken of deviceTokens) {
      if (!(await this.isTokenBlacklisted(deviceToken.fcmToken))) {
        deliverableTokens.push(deviceToken);
      }
    }

    return deliverableTokens;
  }

  private async isTokenBlacklisted(token: string): Promise<boolean> {
    return Boolean(await this.redis.get(`${FCM_TOKEN_BLACKLIST_PREFIX}${token}`));
  }

  private async sendDelivery(
    notification: Notification,
    token: string,
  ): Promise<FcmPushResult | string> {
    try {
      return await this.fcmPushProvider.send({
        token,
        title: notification.title,
        body: notification.body,
        data: this.toFcmData(notification),
      });
    } catch (error) {
      return this.normalizeError(error);
    }
  }

  private async blacklistToken(token: string): Promise<void> {
    await this.redis.set(
      `${FCM_TOKEN_BLACKLIST_PREFIX}${token}`,
      '1',
      FCM_TOKEN_BLACKLIST_TTL_SECONDS,
    );
  }

  private toFcmData(notification: Notification): Record<string, string> {
    const data: Record<string, string> = {
      notificationId: notification.id,
      type: this.toGenericFcmType(notification.type),
      notificationType: notification.type,
    };

    if (
      notification.data &&
      typeof notification.data === 'object' &&
      !Array.isArray(notification.data)
    ) {
      for (const [key, value] of Object.entries(notification.data)) {
        if (
          value === null ||
          value === undefined ||
          ['notificationId', 'type', 'notificationType'].includes(key)
        )
          continue;
        data[key] = typeof value === 'string' ? value : JSON.stringify(value);
      }
    }

    return data;
  }

  private toGenericFcmType(type: Notification['type']): string {
    if (type === 'BOOKING_CREATED' || type === 'BOOKING_CANCELLED') return type;
    if (type === 'TRIP_ASSIGNED') return 'TRIP_ASSIGNED';
    if (
      type.startsWith('TRIP_') ||
      type === 'STOP_DISABLED' ||
      type === 'VEHICLE_SUBSTITUTED' ||
      type === 'VEHICLE_SWAPPED'
    ) {
      return 'TRIP_UPDATE';
    }
    if (type.startsWith('PARCEL_')) return 'PARCEL_UPDATE';
    return 'NOTIFICATION';
  }

  private normalizeError(error: unknown): string {
    return normalizeSafeError(error, FCM_LAST_ERROR_MAX_LENGTH);
  }
}
