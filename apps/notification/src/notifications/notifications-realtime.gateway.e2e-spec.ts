import type { INestApplication } from '@nestjs/common';
import { Test } from '@nestjs/testing';
import { exportSPKI, generateKeyPair, SignJWT, type KeyLike } from 'jose';
import { io, type Socket } from 'socket.io-client';
import { NotificationCorsIoAdapter } from '../app/notification-cors-io.adapter';
import { ENV_TOKEN, NOTIFICATION_JWT_VERIFIER } from '../app/tokens';
import { JoseNotificationUserJwtVerifier } from '../auth/user-jwt.verifier';
import type { Env } from '../config/env.schema';
import { NotificationsRealtimeGateway } from './notifications-realtime.gateway';

const USER_A = '11111111-1111-4111-8111-111111111111';
const USER_B = '22222222-2222-4222-8222-222222222222';
const NOTIFICATION_ID = '33333333-3333-4333-8333-333333333333';
const SOCKET_PATH = '/notification/socket.io';
const CONNECT_TIMEOUT_MS = 2_000;

describe('Notification realtime Socket.IO (e2e)', () => {
  let app: INestApplication;
  let port: number;
  let privateKey: KeyLike;
  let gateway: NotificationsRealtimeGateway;

  beforeAll(async () => {
    const generated = await generateKeyPair('RS256');
    privateKey = generated.privateKey;
    const publicKeyPem = await exportSPKI(generated.publicKey);
    const moduleRef = await Test.createTestingModule({
      providers: [
        NotificationsRealtimeGateway,
        { provide: NOTIFICATION_JWT_VERIFIER, useClass: JoseNotificationUserJwtVerifier },
        { provide: ENV_TOKEN, useValue: createEnv(publicKeyPem) },
      ],
    }).compile();

    gateway = moduleRef.get(NotificationsRealtimeGateway);
    app = moduleRef.createNestApplication();
    app.useWebSocketAdapter(new NotificationCorsIoAdapter(app, '*'));
    await app.listen(0);
    const address = app.getHttpServer().address() as { port: number };
    port = address.port;
  });

  afterAll(async () => {
    await app.close();
  });

  it('connects with an Identity-style token and rejects invalid authentication', async () => {
    const socket = await connect(await signToken(USER_A));
    expect(socket.connected).toBe(true);
    socket.disconnect();

    await expect(connect('invalid-token')).rejects.toThrow('UNAUTHORIZED');
    await expect(
      connect(await signToken(USER_A, Math.floor(Date.now() / 1_000) - 60)),
    ).rejects.toThrow('UNAUTHORIZED');
    await expect(connect()).rejects.toThrow('UNAUTHORIZED');
  });

  it('delivers and replays the stable notification id to every target-user socket only', async () => {
    const socketA1 = await connect(await signToken(USER_A));
    const socketA2 = await connect(await signToken(USER_A));
    const socketB = await connect(await signToken(USER_B));
    const receivedByA1 = waitForEvent<Record<string, unknown>>(socketA1, 'notification:created');
    const receivedByA2 = waitForEvent<Record<string, unknown>>(socketA2, 'notification:created');
    const receivedByB = jest.fn();
    socketB.on('notification:created', receivedByB);

    const notification: Parameters<NotificationsRealtimeGateway['publishCreated']>[0] = {
      id: NOTIFICATION_ID,
      userId: USER_A,
      type: 'BOOKING_CONFIRMED',
      title: 'Đặt vé thành công',
      body: 'Vé của bạn đã được xác nhận.',
      data: null,
      action: { type: 'NONE', params: {} },
      readAt: null,
      createdAt: '2026-08-11T08:30:00.000Z',
    };
    gateway.publishCreated(notification);

    const expectedPayload = expect.objectContaining({
      id: NOTIFICATION_ID,
      createdAt: '2026-08-11T15:30:00.000+07:00',
    });
    await expect(receivedByA1).resolves.toEqual(expectedPayload);
    await expect(receivedByA2).resolves.toEqual(expectedPayload);

    const replayedByA1 = waitForEvent<Record<string, unknown>>(socketA1, 'notification:created');
    const replayedByA2 = waitForEvent<Record<string, unknown>>(socketA2, 'notification:created');
    gateway.publishCreated(notification);
    await expect(replayedByA1).resolves.toEqual(expectedPayload);
    await expect(replayedByA2).resolves.toEqual(expectedPayload);

    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(receivedByB).not.toHaveBeenCalled();

    socketA1.disconnect();
    socketA2.disconnect();
    socketB.disconnect();
  });

  async function signToken(userId: string, expirationTime: string | number = '15m'): Promise<string> {
    return new SignJWT({ role: 'PASSENGER', email: 'socket-test@vietride.local' })
      .setProtectedHeader({ alg: 'RS256', typ: 'JWT', kid: 'notification-e2e-key' })
      .setSubject(userId)
      .setIssuer('vietride-identity')
      .setAudience('vietride-api')
      .setIssuedAt()
      .setExpirationTime(expirationTime)
      .sign(privateKey);
  }

  function connect(token?: string): Promise<Socket> {
    return new Promise((resolve, reject) => {
      const socket = io(`http://127.0.0.1:${port}`, {
        path: SOCKET_PATH,
        auth: token ? { token } : {},
        transports: ['websocket'],
        reconnection: false,
        forceNew: true,
      });
      const timeout = setTimeout(() => {
        socket.disconnect();
        reject(new Error('SOCKET_CONNECT_TIMEOUT'));
      }, CONNECT_TIMEOUT_MS);
      socket.once('connect', () => {
        clearTimeout(timeout);
        resolve(socket);
      });
      socket.once('connect_error', (error) => {
        clearTimeout(timeout);
        socket.disconnect();
        reject(error);
      });
    });
  }

  function waitForEvent<T>(socket: Socket, event: string): Promise<T> {
    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => reject(new Error(`${event}_TIMEOUT`)), CONNECT_TIMEOUT_MS);
      socket.once(event, (payload: T) => {
        clearTimeout(timeout);
        resolve(payload);
      });
    });
  }
});

function createEnv(publicKeyPem: string): Env {
  return {
    NODE_ENV: 'test',
    PORT: 3002,
    DATABASE_URL: 'postgresql://postgres:postgres@localhost:5432/vietride_notification',
    REDIS_URL: 'redis://localhost:6379',
    RABBITMQ_URL: 'amqp://guest:guest@localhost:5672',
    RABBITMQ_EXCHANGE: 'vietride.events',
    INTERNAL_JWT_SECRET: 'notification-e2e-internal-secret',
    JWT_PUBLIC_KEY_URL: 'http://identity.test/v1/.well-known/jwks.json',
    JWT_ISSUER: 'vietride-identity',
    JWT_AUDIENCE: 'vietride-api',
    USER_JWT_PUBLIC_KEY: publicKeyPem,
    IDENTITY_INTERNAL_BASE_URL: 'http://identity.test',
    BOOKING_INTERNAL_BASE_URL: 'http://booking.test',
    PARCEL_INTERNAL_BASE_URL: 'http://parcel.test',
    TRIP_INTERNAL_BASE_URL: 'http://trip.test',
    FCM_DRY_RUN: false,
    FCM_DRY_RUN_TOPIC: 'vietride-e2e-validation',
    SENDGRID_FROM_NAME: 'VietRide',
    NOTIFICATION_RETENTION_DAYS: 90,
    NOTIFICATION_RETENTION_JOB_INTERVAL_MS: 86_400_000,
    NOTIFICATION_CORS_ORIGIN: '*',
    LOG_LEVEL: 'info',
  } as Env;
}
