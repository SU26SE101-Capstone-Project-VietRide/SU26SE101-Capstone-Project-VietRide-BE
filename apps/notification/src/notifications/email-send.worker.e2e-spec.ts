import { Test } from '@nestjs/testing';
import type { Job } from 'bullmq';
import { EMAIL_PROVIDER, ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import {
  EmailDeliveryStatus,
  EmailTemplateKey,
  type EmailDelivery,
} from '../generated/notification-prisma-client';
import type { EmailProvider, EmailSendJobData } from './email-send.types';
import { EmailSendWorker } from './email-send.worker';
import { EmailTemplateRenderer } from './email-template.renderer';
import { NotificationsRepository } from './notifications.repository';

const EMAIL_DELIVERY_ID = '11111111-1111-4111-8111-111111111111';
const RECIPIENT_EMAIL = 'operator@vietride.local';

describe('EmailSendWorker pipeline (e2e)', () => {
  it('renders an invoice notice and sends it through provider abstraction', async () => {
    const repository = {
      findEmailDeliveryById: jest.fn(async () => createEmailDelivery()),
      markEmailDeliverySent: jest.fn(),
    };
    const emailProvider: jest.Mocked<EmailProvider> = {
      send: jest.fn(async (payload: Parameters<EmailProvider['send']>[0]) => ({
        messageId: `sendgrid-e2e:${payload.toEmail}`,
      })),
    };

    const moduleRef = await Test.createTestingModule({
      providers: [
        EmailSendWorker,
        EmailTemplateRenderer,
        { provide: ENV_TOKEN, useValue: createEnv() },
        { provide: EMAIL_PROVIDER, useValue: emailProvider },
        { provide: NotificationsRepository, useValue: repository },
      ],
    }).compile();

    const worker = moduleRef.get(EmailSendWorker);
    await worker.process(createJob());

    expect(emailProvider.send).toHaveBeenCalledWith(
      expect.objectContaining({
        toEmail: RECIPIENT_EMAIL,
        subject: 'Hoa don VietRide VR-INV-202606-000001',
        text: expect.stringContaining('VR-INV-202606-000001'),
      }),
    );
    expect(repository.markEmailDeliverySent).toHaveBeenCalledWith(
      EMAIL_DELIVERY_ID,
      `sendgrid-e2e:${RECIPIENT_EMAIL}`,
    );

    await moduleRef.close();
  });
});

function createJob(): Job<EmailSendJobData> {
  return {
    attemptsMade: 0,
    data: {
      emailDeliveryId: EMAIL_DELIVERY_ID,
      toEmail: RECIPIENT_EMAIL,
      templateKey: EmailTemplateKey.INVOICE_NOTICE,
      templateData: {
        invoiceNumber: 'VR-INV-202606-000001',
        amountVnd: 500000,
        invoiceUrl: 'https://app.vietride.local/invoices/token-redacted-in-db',
      },
    },
  } as unknown as Job<EmailSendJobData>;
}

function createEmailDelivery(): EmailDelivery {
  return {
    id: EMAIL_DELIVERY_ID,
    notificationId: null,
    toEmail: RECIPIENT_EMAIL,
    templateKey: EmailTemplateKey.INVOICE_NOTICE,
    subject: 'Hoa don VietRide VR-INV-202606-000001',
    sanitizedData: {
      invoiceNumber: 'VR-INV-202606-000001',
      amountVnd: 500000,
      invoiceUrl: 'http...[REDACTED]...n-db',
    },
    status: EmailDeliveryStatus.PENDING,
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
    IDENTITY_INTERNAL_BASE_URL: 'http://identity.test',
    SENDGRID_API_KEY: undefined,
    SENDGRID_FROM_EMAIL: undefined,
    SENDGRID_FROM_NAME: 'VietRide',
    NOTIFICATION_RETENTION_DAYS: 90,
    NOTIFICATION_RETENTION_JOB_INTERVAL_MS: 86_400_000,
  };
}
