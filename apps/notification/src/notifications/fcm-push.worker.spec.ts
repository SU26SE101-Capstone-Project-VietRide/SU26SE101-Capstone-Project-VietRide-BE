import { RedisService } from '@vietride/nest-redis';
import type { Job } from 'bullmq';
import type { Env } from '../config/env.schema';
import {
  DevicePlatform,
  NotificationDeliveryStatus,
  NotificationType,
  type Notification,
  type NotificationDelivery,
} from '../generated/notification-prisma-client';
import {
  FCM_PUSH_ATTEMPTS,
  FCM_TOKEN_BLACKLIST_PREFIX,
  FCM_TOKEN_BLACKLIST_TTL_SECONDS,
} from './fcm-push.constants';
import type { DeviceTokenProvider, FcmPushJobData, FcmPushProvider } from './fcm-push.types';
import { FcmPushWorker } from './fcm-push.worker';
import { NotificationsRepository } from './notifications.repository';

const USER_ID = '11111111-1111-4111-8111-111111111111';
const NOTIFICATION_ID = '22222222-2222-4222-8222-222222222222';
const DELIVERY_ID = '33333333-3333-4333-8333-333333333333';
const FCM_TOKEN = 'fcm-token-redacted-in-logs';

describe('FcmPushWorker', () => {
  let repository: jest.Mocked<NotificationsRepository>;
  let deviceTokenProvider: jest.Mocked<DeviceTokenProvider>;
  let fcmPushProvider: jest.Mocked<FcmPushProvider>;
  let redis: jest.Mocked<RedisService>;
  let worker: FcmPushWorker;

  beforeEach(() => {
    repository = {
      markDeliveryValidated: jest.fn(),
      findById: jest.fn(),
      listDeliveriesByNotificationId: jest.fn(),
      createDelivery: jest.fn(),
      markDeliverySent: jest.fn(),
      markDeliveryRetrying: jest.fn(),
      markDeliveryFailed: jest.fn(),
    } as unknown as jest.Mocked<NotificationsRepository>;
    deviceTokenProvider = {
      listActiveDeviceTokens: jest.fn(),
      deactivateDeviceToken: jest.fn(),
    };
    fcmPushProvider = {
      send: jest.fn(),
    };
    redis = {
      get: jest.fn(),
      set: jest.fn(),
    } as unknown as jest.Mocked<RedisService>;
    deviceTokenProvider.listActiveDeviceTokens.mockResolvedValue([]);
    worker = new FcmPushWorker(createEnv(), deviceTokenProvider, fcmPushProvider, repository, redis);
  });

  it('creates delivery audit rows and marks successful sends as SENT', async () => {
    repository.findById.mockResolvedValue(createNotification());
    repository.listDeliveriesByNotificationId.mockResolvedValue([createDelivery()]);
    repository.createDelivery.mockResolvedValue(createDelivery());
    deviceTokenProvider.listActiveDeviceTokens.mockResolvedValue([
      { fcmToken: FCM_TOKEN, platform: DevicePlatform.ANDROID },
    ]);
    redis.get.mockResolvedValue(null);
    fcmPushProvider.send.mockResolvedValue({ messageId: 'firebase-message-id' });

    await worker.process(createJob(0));

    expect(repository.createDelivery).toHaveBeenCalledWith(NOTIFICATION_ID, {
      fcmToken: FCM_TOKEN,
      platform: DevicePlatform.ANDROID,
    });
    expect(fcmPushProvider.send).toHaveBeenCalledWith({
      token: FCM_TOKEN,
      title: 'Dat ve thanh cong',
      body: 'Ve cua ban da duoc xac nhan.',
      data: expect.objectContaining({
        notificationId: NOTIFICATION_ID,
        type: 'NOTIFICATION',
        notificationType: NotificationType.BOOKING_CONFIRMED,
        bookingId: 'VR123',
      }),
    });
    expect(repository.markDeliverySent).toHaveBeenCalledWith(DELIVERY_ID, 'firebase-message-id');
  });

  it('blacklists invalid tokens and marks delivery as FAILED', async () => {
    repository.findById.mockResolvedValue(createNotification());
    repository.listDeliveriesByNotificationId.mockResolvedValue([createDelivery()]);
    fcmPushProvider.send.mockResolvedValue({ invalidToken: true });

    await worker.process(createJob(0));

    expect(redis.set).toHaveBeenCalledWith(
      `${FCM_TOKEN_BLACKLIST_PREFIX}${FCM_TOKEN}`,
      '1',
      FCM_TOKEN_BLACKLIST_TTL_SECONDS,
    );
    expect(deviceTokenProvider.deactivateDeviceToken).toHaveBeenCalledWith(USER_ID, FCM_TOKEN);
    expect(repository.markDeliveryFailed).toHaveBeenCalledWith(DELIVERY_ID, 1, 'FCM_TOKEN_INVALID');
  });

  it('marks retryable failures as RETRYING before BullMQ retries the job', async () => {
    repository.findById.mockResolvedValue(createNotification());
    repository.listDeliveriesByNotificationId.mockResolvedValue([createDelivery()]);
    fcmPushProvider.send.mockRejectedValue(new Error('firebase temporary outage'));

    await expect(worker.process(createJob(1))).rejects.toThrow('FCM_PUSH_RETRYABLE_FAILURE');

    expect(repository.markDeliveryRetrying).toHaveBeenCalledWith(
      DELIVERY_ID,
      2,
      'firebase temporary outage',
    );
  });

  it('marks retryable failures as FAILED on the last attempt', async () => {
    repository.findById.mockResolvedValue(createNotification());
    repository.listDeliveriesByNotificationId.mockResolvedValue([createDelivery()]);
    fcmPushProvider.send.mockRejectedValue(new Error('firebase exhausted'));

    await expect(worker.process(createJob(FCM_PUSH_ATTEMPTS - 1))).rejects.toThrow('FCM_PUSH_RETRYABLE_FAILURE');

    expect(repository.markDeliveryFailed).toHaveBeenCalledWith(
      DELIVERY_ID,
      FCM_PUSH_ATTEMPTS,
      'firebase exhausted',
    );
  });
});

function createJob(attemptsMade: number): Job<FcmPushJobData> {
  return {
    attemptsMade,
    data: {
      notificationId: NOTIFICATION_ID,
      userId: USER_ID,
    },
  } as Job<FcmPushJobData>;
}

function createNotification(): Notification {
  return {
    id: NOTIFICATION_ID,
    userId: USER_ID,
    type: NotificationType.BOOKING_CONFIRMED,
    title: 'Dat ve thanh cong',
    body: 'Ve cua ban da duoc xac nhan.',
    data: { bookingId: 'VR123' },
    dedupeKey: null,
    readAt: null,
    createdAt: new Date('2026-06-01T10:00:00.000Z'),
  };
}

function createDelivery(): NotificationDelivery {
  return {
    id: DELIVERY_ID,
    notificationId: NOTIFICATION_ID,
    fcmToken: FCM_TOKEN,
    platform: DevicePlatform.ANDROID,
    status: NotificationDeliveryStatus.PENDING,
    retryCount: 0,
    lastError: null,
    providerMessageId: null,
    sentAt: null,
    createdAt: new Date('2026-06-01T10:00:00.000Z'),
    updatedAt: new Date('2026-06-01T10:00:00.000Z'),
  };
}

function createEnv(): Env {
  return {
    NODE_ENV: 'test',
    PORT: 3002,
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
    DATABASE_URL: 'postgresql://postgres:postgres@localhost:5432/vietride_notification',
    LOG_LEVEL: 'info',
    TRIP_INTERNAL_BASE_URL: 'http://trip.test',
    IDENTITY_INTERNAL_BASE_URL: 'http://identity.test',
    FCM_PROJECT_ID: undefined,
    FCM_CLIENT_EMAIL: undefined,
    FCM_PRIVATE_KEY: undefined,
    FCM_DRY_RUN: false,
    FCM_DRY_RUN_TOPIC: 'vietride-e2e-validation',
    SENDGRID_API_KEY: undefined,
    SENDGRID_FROM_EMAIL: undefined,
    SENDGRID_FROM_NAME: 'VietRide',
    NOTIFICATION_RETENTION_DAYS: 90,
    NOTIFICATION_RETENTION_JOB_INTERVAL_MS: 86_400_000,
  };
}
