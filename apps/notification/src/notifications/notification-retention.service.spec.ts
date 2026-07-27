import type { Env } from '../config/env.schema';
import { NotificationRetentionService } from './notification-retention.service';
import { NotificationsRepository } from './notifications.repository';

describe('NotificationRetentionService', () => {
  let repository: jest.Mocked<NotificationsRepository>;
  let service: NotificationRetentionService;

  beforeEach(() => {
    repository = {
      deleteNotificationsCreatedBefore: jest.fn(),
    } as unknown as jest.Mocked<NotificationsRepository>;
    service = new NotificationRetentionService(createEnv(), repository);
  });

  afterEach(() => {
    service.onModuleDestroy();
  });

  it('calculates cutoff from configurable retention days', () => {
    expect(service.calculateCutoff(new Date('2026-06-09T00:00:00.000Z')).toISOString()).toBe(
      '2026-03-11T00:00:00.000Z',
    );
  });

  it('deletes notifications older than cutoff and returns deleted count', async () => {
    repository.deleteNotificationsCreatedBefore.mockResolvedValue(7);

    await expect(service.runRetention(new Date('2026-06-09T00:00:00.000Z'))).resolves.toBe(7);
    expect(repository.deleteNotificationsCreatedBefore).toHaveBeenCalledWith(
      new Date('2026-03-11T00:00:00.000Z'),
    );
  });
});

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
    BOOKING_INTERNAL_BASE_URL: 'http://booking.test',
    PARCEL_INTERNAL_BASE_URL: 'http://parcel.test',
    IDENTITY_INTERNAL_BASE_URL: 'http://identity.test',
    FCM_DRY_RUN: false,
    FCM_DRY_RUN_TOPIC: 'vietride-e2e-validation',
    SENDGRID_API_KEY: undefined,
    SENDGRID_FROM_EMAIL: undefined,
    SENDGRID_FROM_NAME: 'VietRide',
    NOTIFICATION_RETENTION_DAYS: 90,
    NOTIFICATION_RETENTION_JOB_INTERVAL_MS: 86_400_000,
  };
}
