import { Test } from '@nestjs/testing';
import { RedisService } from '@vietride/nest-redis';
import type { Job } from 'bullmq';
import { DEVICE_TOKEN_PROVIDER, ENV_TOKEN, FCM_PUSH_PROVIDER } from '../app/tokens';
import type { Env } from '../config/env.schema';
import {
  DevicePlatform,
  NotificationDeliveryStatus,
  NotificationType,
  type Notification,
  type NotificationDelivery,
} from '../generated/notification-prisma-client';
import type { FcmPushJobData } from './fcm-push.types';
import { FcmPushWorker } from './fcm-push.worker';
import { NotificationsRepository } from './notifications.repository';

const USER_ID = '11111111-1111-4111-8111-111111111111';
const NOTIFICATION_ID = '22222222-2222-4222-8222-222222222222';
const DELIVERY_ID = '33333333-3333-4333-8333-333333333333';

describe('FcmPushWorker pipeline (e2e)', () => {
  it('resolves device tokens, creates delivery audit, and sends through provider abstraction', async () => {
    const repository = {
      findById: jest.fn(async () => createNotification()),
      listDeliveriesByNotificationId: jest.fn(async () => []),
      createDelivery: jest.fn(async () => createDelivery()),
      markDeliverySent: jest.fn(),
    };
    const deviceTokenProvider = {
      listActiveDeviceTokens: jest.fn(async () => [
        { fcmToken: 'fcm-e2e-token', platform: DevicePlatform.ANDROID },
      ]),
    };
    const fcmProvider = {
      send: jest.fn(async () => ({ messageId: 'firebase-e2e-message-id' })),
    };

    const moduleRef = await Test.createTestingModule({
      providers: [
        FcmPushWorker,
        { provide: ENV_TOKEN, useValue: createEnv() },
        { provide: DEVICE_TOKEN_PROVIDER, useValue: deviceTokenProvider },
        { provide: FCM_PUSH_PROVIDER, useValue: fcmProvider },
        { provide: NotificationsRepository, useValue: repository },
        { provide: RedisService, useValue: { get: jest.fn(async () => null), set: jest.fn() } },
      ],
    }).compile();

    const worker = moduleRef.get(FcmPushWorker);
    await worker.process(createJob());

    expect(deviceTokenProvider.listActiveDeviceTokens).toHaveBeenCalledWith(USER_ID);
    expect(repository.createDelivery).toHaveBeenCalledWith(NOTIFICATION_ID, {
      fcmToken: 'fcm-e2e-token',
      platform: DevicePlatform.ANDROID,
    });
    expect(fcmProvider.send).toHaveBeenCalledWith(
      expect.objectContaining({
        token: 'fcm-e2e-token',
        title: 'Dat ve thanh cong',
      }),
    );
    expect(repository.markDeliverySent).toHaveBeenCalledWith(DELIVERY_ID);

    await moduleRef.close();
  });
});

function createJob(): Job<FcmPushJobData> {
  return {
    attemptsMade: 0,
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
    readAt: null,
    createdAt: new Date('2026-06-01T10:00:00.000Z'),
  };
}

function createDelivery(): NotificationDelivery {
  return {
    id: DELIVERY_ID,
    notificationId: NOTIFICATION_ID,
    fcmToken: 'fcm-e2e-token',
    platform: DevicePlatform.ANDROID,
    status: NotificationDeliveryStatus.PENDING,
    retryCount: 0,
    lastError: null,
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
    IDENTITY_INTERNAL_BASE_URL: 'http://identity.test',
    SENDGRID_API_KEY: undefined,
    SENDGRID_FROM_EMAIL: undefined,
    SENDGRID_FROM_NAME: 'VietRide',
  };
}
