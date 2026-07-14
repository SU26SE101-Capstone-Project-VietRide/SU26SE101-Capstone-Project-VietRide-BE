import { INestApplication } from '@nestjs/common';
import { APP_FILTER, APP_INTERCEPTOR } from '@nestjs/core';
import { Test } from '@nestjs/testing';
import { ApiResponseExceptionFilter, ApiResponseInterceptor } from '@vietride/nest-common';
import { exportSPKI, generateKeyPair, SignJWT, type KeyLike } from 'jose';
import { ENV_TOKEN, NOTIFICATION_JWT_VERIFIER } from '../app/tokens';
import { JoseNotificationUserJwtVerifier } from '../auth/user-jwt.verifier';
import type { Env } from '../config/env.schema';
import { NotificationPrismaService } from '../prisma/notification-prisma.service';
import { EmailSendQueue } from './email-send.queue';
import { EmailTemplateRenderer } from './email-template.renderer';
import { FcmPushQueue } from './fcm-push.queue';
import { NotificationsController } from './notifications.controller';
import { NotificationsRepository } from './notifications.repository';
import { NotificationsService } from './notifications.service';

const OWNER_USER_ID = '11111111-1111-4111-8111-111111111111';
const OTHER_USER_ID = '22222222-2222-4222-8222-222222222222';
const NOTIFICATION_ID = '33333333-3333-4333-8333-333333333333';
const OTHER_NOTIFICATION_ID = '44444444-4444-4444-8444-444444444444';
const IDENTITY_ISSUER = 'vietride-identity';
const IDENTITY_AUDIENCE = 'vietride-api';

interface ApiEnvelope<TData> {
  success: boolean;
  statusCode: number;
  data?: TData;
  error?: {
    code: string;
    message: string;
  };
}

describe('NotificationsController (e2e)', () => {
  let app: INestApplication;
  let port: number;
  let privateKey: KeyLike;
  let findMany: jest.Mock;
  let count: jest.Mock;
  let findFirst: jest.Mock;
  let update: jest.Mock;

  beforeAll(async () => {
    const generated = await generateKeyPair('RS256');
    privateKey = generated.privateKey;
    const publicKeyPem = await exportSPKI(generated.publicKey);

    findMany = jest.fn(async () => [createNotification({ readAt: null })]);
    count = jest.fn(async () => 1);
    findFirst = jest.fn(async (args: { where: { id: string; userId: string } }) => {
      if (args.where.id === NOTIFICATION_ID && args.where.userId === OWNER_USER_ID) {
        return createNotification({ readAt: null });
      }
      return null;
    });
    update = jest.fn(async () =>
      createNotification({ readAt: new Date('2026-06-01T10:01:00.000Z') }),
    );

    const moduleRef = await Test.createTestingModule({
      controllers: [NotificationsController],
      providers: [
        NotificationsService,
        NotificationsRepository,
        { provide: FcmPushQueue, useValue: { enqueue: jest.fn() } },
        { provide: EmailSendQueue, useValue: { enqueue: jest.fn() } },
        EmailTemplateRenderer,
        { provide: ENV_TOKEN, useValue: createTestEnv(publicKeyPem) },
        { provide: NOTIFICATION_JWT_VERIFIER, useClass: JoseNotificationUserJwtVerifier },
        {
          provide: NotificationPrismaService,
          useValue: {
            notification: {
              findMany,
              count,
              findFirst,
              update,
            },
          },
        },
        { provide: APP_FILTER, useValue: new ApiResponseExceptionFilter() },
        { provide: APP_INTERCEPTOR, useValue: new ApiResponseInterceptor() },
      ],
    }).compile();

    app = moduleRef.createNestApplication();
    await app.listen(0);
    port = readListeningPort(app);
  });

  afterAll(async () => {
    if (app) await app.close();
  });

  it('returns 401 envelope when auth is missing', async () => {
    const response = await getJson<ApiEnvelope<unknown>>('/v1/notifications');

    expect(response.status).toBe(401);
    expect(response.body.success).toBe(false);
    expect(response.body.error?.code).toBe('UNAUTHORIZED');
  });

  it('returns 401 envelope when token is invalid', async () => {
    const response = await getJson<ApiEnvelope<unknown>>('/v1/notifications', 'not-a-jwt');

    expect(response.status).toBe(401);
    expect(response.body.error?.code).toBe('UNAUTHORIZED');
  });

  it('returns 400 envelope when query options are invalid', async () => {
    const token = await signIdentityToken(OWNER_USER_ID);
    const response = await getJson<ApiEnvelope<unknown>>('/v1/notifications?pageSize=101', token);

    expect(response.status).toBe(400);
    expect(response.body.success).toBe(false);
  });

  it('returns owner notification history', async () => {
    const token = await signIdentityToken(OWNER_USER_ID);
    const response = await getJson<
      ApiEnvelope<{ items: Array<{ id: string; readAt: string | null }> }>
    >('/v1/notifications?unreadOnly=true&page=1&pageSize=20&sortBy=createdAt&sortDir=desc', token);

    expect(response.status).toBe(200);
    expect(response.body.success).toBe(true);
    expect(findMany).toHaveBeenCalledWith(
      expect.objectContaining({
        where: { userId: OWNER_USER_ID, readAt: null },
        orderBy: { createdAt: 'desc' },
        skip: 0,
        take: 20,
      }),
    );
    expect(response.body.data?.items[0]).toEqual(
      expect.objectContaining({
        id: NOTIFICATION_ID,
        readAt: null,
      }),
    );
  });

  it('returns 204 when owner marks notification as read', async () => {
    const token = await signIdentityToken(OWNER_USER_ID);
    const response = await post(`/v1/notifications/${NOTIFICATION_ID}/read`, token);

    expect(response.status).toBe(204);
    expect(update).toHaveBeenCalledWith(
      expect.objectContaining({
        where: { id: NOTIFICATION_ID },
        data: expect.objectContaining({ readAt: expect.any(Date) }),
      }),
    );
  });

  it('returns 404 when user marks another user notification as read', async () => {
    const token = await signIdentityToken(OTHER_USER_ID);
    const response = await postJson<ApiEnvelope<unknown>>(
      `/v1/notifications/${OTHER_NOTIFICATION_ID}/read`,
      token,
    );

    expect(response.status).toBe(404);
    expect(response.body.error?.code).toBe('NOTIFICATION_NOT_FOUND');
  });

  it('does not expose the old PATCH mark-read route', async () => {
    const token = await signIdentityToken(OWNER_USER_ID);
    const response = await patch(`/v1/notifications/${NOTIFICATION_ID}`, token);

    expect(response.status).toBe(404);
  });

  async function signIdentityToken(userId: string): Promise<string> {
    return new SignJWT({
      role: 'PASSENGER',
      email: 'notification-rest-test@vietride.local',
    })
      .setProtectedHeader({ alg: 'RS256', typ: 'JWT', kid: 'notification-rest-e2e-key' })
      .setSubject(userId)
      .setIssuer(IDENTITY_ISSUER)
      .setAudience(IDENTITY_AUDIENCE)
      .setIssuedAt()
      .setExpirationTime('15m')
      .sign(privateKey);
  }

  async function getJson<TBody>(
    path: string,
    token?: string,
  ): Promise<{ status: number; body: TBody }> {
    const response = await fetch(`http://127.0.0.1:${port}${path}`, {
      headers: token ? { Authorization: `Bearer ${token}` } : {},
    });
    return {
      status: response.status,
      body: (await response.json()) as TBody,
    };
  }

  async function patch(path: string, token: string): Promise<Response> {
    return fetch(`http://127.0.0.1:${port}${path}`, {
      method: 'PATCH',
      headers: {
        Authorization: `Bearer ${token}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ read: true }),
    });
  }

  async function post(path: string, token: string): Promise<Response> {
    return fetch(`http://127.0.0.1:${port}${path}`, {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${token}`,
      },
    });
  }

  async function postJson<TBody>(
    path: string,
    token: string,
  ): Promise<{ status: number; body: TBody }> {
    const response = await post(path, token);
    return {
      status: response.status,
      body: (await response.json()) as TBody,
    };
  }
});

function createNotification(overrides: Record<string, unknown>) {
  return {
    id: NOTIFICATION_ID,
    userId: OWNER_USER_ID,
    type: 'BOOKING_CONFIRMED',
    title: 'Dat ve thanh cong',
    body: 'Ve cua ban da duoc xac nhan.',
    data: { bookingId: '55555555-5555-4555-8555-555555555555' },
    dedupeKey: null,
    readAt: null,
    createdAt: new Date('2026-06-01T10:00:00.000Z'),
    ...overrides,
  };
}

function createTestEnv(publicKeyPem: string): Env {
  return {
    NODE_ENV: 'test',
    PORT: 3002,
    GATEWAY_URL: 'http://gateway:3000',
    INTERNAL_JWT_SECRET: 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa',
    INTERNAL_JWT_TTL_SEC: 120,
    JWT_PUBLIC_KEY_URL: 'http://identity.test/v1/.well-known/jwks.json',
    JWT_ISSUER: IDENTITY_ISSUER,
    JWT_AUDIENCE: IDENTITY_AUDIENCE,
    REDIS_URL: 'redis://localhost:6379',
    REDIS_HOST: 'localhost',
    REDIS_PORT: 6379,
    RABBITMQ_URL: 'amqp://guest:guest@localhost:5672',
    RABBITMQ_EXCHANGE: 'vietride.events',
    DATABASE_URL: 'postgresql://postgres:postgres@localhost:5432/vietride_notification',
    LOG_LEVEL: 'info',
    USER_JWT_PUBLIC_KEY: publicKeyPem,
    IDENTITY_INTERNAL_BASE_URL: 'http://identity.test',
    TRIP_INTERNAL_BASE_URL: 'http://trip.test',
    FCM_PROJECT_ID: undefined,
    FCM_CLIENT_EMAIL: undefined,
    FCM_PRIVATE_KEY: undefined,
    FCM_DRY_RUN: false,
    FCM_DRY_RUN_TOPIC: 'vietride-notification-e2e',
    SENDGRID_API_KEY: undefined,
    SENDGRID_FROM_EMAIL: undefined,
    SENDGRID_FROM_NAME: 'VietRide',
    NOTIFICATION_RETENTION_DAYS: 90,
    NOTIFICATION_RETENTION_JOB_INTERVAL_MS: 86_400_000,
  };
}

function readListeningPort(app: INestApplication): number {
  const address = app.getHttpServer().address();
  if (typeof address === 'object' && address !== null) {
    return address.port;
  }

  throw new Error('NOTIFICATION_REST_E2E_PORT_UNAVAILABLE');
}
