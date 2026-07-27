import type { Job } from 'bullmq';
import type { Env } from '../config/env.schema';
import {
  EmailDeliveryStatus,
  EmailTemplateKey,
  type EmailDelivery,
} from '../generated/notification-prisma-client';
import { EMAIL_SENDING_LEASE_MS, EMAIL_SEND_ATTEMPTS } from './email-send.constants';
import type { EmailProvider, EmailSendJobData } from './email-send.types';
import { EmailSendWorker } from './email-send.worker';
import { EmailTemplateRenderer } from './email-template.renderer';
import { NotificationsRepository } from './notifications.repository';

const EMAIL_DELIVERY_ID = '11111111-1111-4111-8111-111111111111';
const RECIPIENT_EMAIL = 'passenger@vietride.local';

describe('EmailSendWorker', () => {
  let repository: jest.Mocked<NotificationsRepository>;
  let emailProvider: jest.Mocked<EmailProvider>;
  let worker: EmailSendWorker;

  beforeEach(() => {
    jest.useFakeTimers().setSystemTime(new Date('2026-06-01T10:10:00.000Z'));
    repository = {
      findEmailDeliveryById: jest.fn(),
      markEmailDeliverySending: jest.fn().mockResolvedValue(true),
      markEmailDeliverySent: jest.fn().mockResolvedValue(true),
      markEmailDeliveryRetrying: jest.fn().mockResolvedValue(true),
      markEmailDeliveryFailed: jest.fn().mockResolvedValue(true),
    } as unknown as jest.Mocked<NotificationsRepository>;
    emailProvider = {
      send: jest.fn(),
    };
    worker = new EmailSendWorker(
      createEnv(),
      emailProvider,
      new EmailTemplateRenderer(),
      repository,
    );
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it('renders AUTH_OTP and marks delivery as SENT without reading secrets from audit data', async () => {
    repository.findEmailDeliveryById.mockResolvedValue(createEmailDelivery());
    emailProvider.send.mockResolvedValue({ messageId: 'sendgrid-message-id' });

    await worker.process(createJob(0));

    expect(emailProvider.send).toHaveBeenCalledWith(
      expect.objectContaining({
        toEmail: RECIPIENT_EMAIL,
        subject: 'Mã xác thực VietRide',
        text: expect.stringContaining('123456'),
      }),
    );
    expect(repository.markEmailDeliverySent).toHaveBeenCalledWith(
      EMAIL_DELIVERY_ID,
      'sendgrid-message-id',
      new Date('2026-06-01T10:10:00.000Z'),
    );
  });

  it('marks provider failures as RETRYING before BullMQ retries the job', async () => {
    repository.findEmailDeliveryById.mockResolvedValue(createEmailDelivery());
    emailProvider.send.mockRejectedValue(new Error('sendgrid temporary outage'));

    await expect(worker.process(createJob(1))).rejects.toThrow('EMAIL_SEND_RETRYABLE_FAILURE');

    expect(repository.markEmailDeliveryRetrying).toHaveBeenCalledWith(
      EMAIL_DELIVERY_ID,
      2,
      'sendgrid temporary outage',
      new Date('2026-06-01T10:10:00.000Z'),
    );
  });

  it('marks provider failures as FAILED on the last attempt', async () => {
    repository.findEmailDeliveryById.mockResolvedValue(createEmailDelivery());
    emailProvider.send.mockRejectedValue(new Error('sendgrid exhausted'));

    await expect(worker.process(createJob(EMAIL_SEND_ATTEMPTS - 1))).resolves.toBeUndefined();

    expect(repository.markEmailDeliveryFailed).toHaveBeenCalledWith(
      EMAIL_DELIVERY_ID,
      EMAIL_SEND_ATTEMPTS,
      'sendgrid exhausted',
      new Date('2026-06-01T10:10:00.000Z'),
    );
  });

  it('skips deliveries already marked SENT', async () => {
    repository.findEmailDeliveryById.mockResolvedValue(
      createEmailDelivery({ status: EmailDeliveryStatus.SENT }),
    );

    await worker.process(createJob(0));

    expect(emailProvider.send).not.toHaveBeenCalled();
  });

  it('keeps a fresh SENDING lease retryable without calling the provider', async () => {
    repository.findEmailDeliveryById.mockResolvedValue(
      createEmailDelivery({
        status: EmailDeliveryStatus.SENDING,
        updatedAt: new Date(Date.now() - EMAIL_SENDING_LEASE_MS + 1),
      }),
    );

    await expect(worker.process(createJob(1))).rejects.toThrow('EMAIL_SEND_LEASE_ACTIVE');

    expect(repository.markEmailDeliverySending).not.toHaveBeenCalled();
    expect(emailProvider.send).not.toHaveBeenCalled();
  });

  it('reclaims a stale SENDING lease and resends with at-least-once semantics', async () => {
    repository.findEmailDeliveryById.mockResolvedValue(
      createEmailDelivery({
        status: EmailDeliveryStatus.SENDING,
        updatedAt: new Date(Date.now() - EMAIL_SENDING_LEASE_MS),
      }),
    );
    emailProvider.send.mockResolvedValue({ messageId: 'recovered-message-id' });

    await worker.process(createJob(EMAIL_SEND_ATTEMPTS - 1));

    expect(repository.markEmailDeliverySending).toHaveBeenCalledWith(
      EMAIL_DELIVERY_ID,
      new Date(Date.now() - EMAIL_SENDING_LEASE_MS),
      new Date('2026-06-01T10:10:00.000Z'),
    );
    expect(emailProvider.send).toHaveBeenCalledTimes(1);
    expect(repository.markEmailDeliverySent).toHaveBeenCalledWith(
      EMAIL_DELIVERY_ID,
      'recovered-message-id',
      new Date('2026-06-01T10:10:00.000Z'),
    );
  });

  it('does not let an expired worker overwrite a newer lease after provider failure', async () => {
    repository.findEmailDeliveryById.mockResolvedValue(createEmailDelivery());
    repository.markEmailDeliveryRetrying.mockResolvedValue(false);
    emailProvider.send.mockRejectedValue(new Error('late provider failure'));

    await expect(worker.process(createJob(0))).resolves.toBeUndefined();

    expect(repository.markEmailDeliveryRetrying).toHaveBeenCalledWith(
      EMAIL_DELIVERY_ID,
      1,
      'late provider failure',
      new Date('2026-06-01T10:10:00.000Z'),
    );
  });
});

function createJob(attemptsMade: number): Job<EmailSendJobData> {
  return {
    attemptsMade,
    data: {
      emailDeliveryId: EMAIL_DELIVERY_ID,
      toEmail: RECIPIENT_EMAIL,
      templateKey: EmailTemplateKey.AUTH_OTP,
      templateData: {
        otpCode: '123456',
        purpose: 'dang ky',
        ttlMinutes: 10,
      },
    },
  } as unknown as Job<EmailSendJobData>;
}

function createEmailDelivery(overrides: Partial<EmailDelivery> = {}): EmailDelivery {
  return {
    id: EMAIL_DELIVERY_ID,
    notificationId: null,
    dedupeKey: null,
    toEmail: RECIPIENT_EMAIL,
    templateKey: EmailTemplateKey.AUTH_OTP,
    subject: 'Mã xác thực VietRide',
    sanitizedData: { otpCode: '[REDACTED]' },
    status: EmailDeliveryStatus.PENDING,
    retryCount: 0,
    lastError: null,
    providerMessageId: null,
    sentAt: null,
    createdAt: new Date('2026-06-01T10:00:00.000Z'),
    updatedAt: new Date('2026-06-01T10:00:00.000Z'),
    ...overrides,
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
