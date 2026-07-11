import { jwtVerify } from 'jose';
import type { Env } from '../config/env.schema';
import { DevicePlatform } from '../generated/notification-prisma-client';
import {
  INTERNAL_AUTH_HEADER,
  INTERNAL_JWT_AUDIENCE,
  INTERNAL_JWT_ISSUER,
} from './fcm-push.constants';
import { IdentityDeviceTokenProvider } from './identity-device-token.provider';

const USER_ID = '11111111-1111-4111-8111-111111111111';
const INTERNAL_JWT_SECRET = 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa';

describe('IdentityDeviceTokenProvider', () => {
  const originalFetch = global.fetch;

  afterEach(() => {
    global.fetch = originalFetch;
    jest.restoreAllMocks();
  });

  it('calls Identity device-token endpoint with VietRide internal JWT', async () => {
    let capturedHeaders: Record<string, string> | undefined;
    global.fetch = jest.fn(async (_input: string | URL, init?: RequestInit) => {
      capturedHeaders = init?.headers as Record<string, string>;
      return new Response(
        JSON.stringify([{ fcmToken: 'active-android-token', platform: DevicePlatform.ANDROID }]),
        { status: 200 },
      );
    }) as typeof fetch;
    const provider = new IdentityDeviceTokenProvider(createEnv());

    await expect(provider.listActiveDeviceTokens(USER_ID)).resolves.toEqual([
      { fcmToken: 'active-android-token', platform: DevicePlatform.ANDROID },
    ]);

    const authorization = readHeader(capturedHeaders, INTERNAL_AUTH_HEADER);
    expect(authorization).toMatch(/^Bearer /);
    const token = authorization.replace('Bearer ', '');
    const verified = await jwtVerify(token, new TextEncoder().encode(INTERNAL_JWT_SECRET), {
      issuer: INTERNAL_JWT_ISSUER,
      audience: INTERNAL_JWT_AUDIENCE,
    });
    expect(verified.payload.sub).toBe('notification-service');
    expect(global.fetch).toHaveBeenCalledWith(
      new URL(`/internal/v1/users/${USER_ID}/device-tokens`, 'http://identity.test'),
      expect.any(Object),
    );
  });
});

function readHeader(headers: Record<string, string> | undefined, name: string): string {
  if (!headers) {
    throw new Error('EXPECTED_PLAIN_HEADERS_OBJECT');
  }

  const value = headers[name];
  if (typeof value !== 'string') {
    throw new Error(`EXPECTED_HEADER_${name}`);
  }

  return value;
}

function createEnv(): Env {
  return {
    NODE_ENV: 'test',
    PORT: 3002,
    GATEWAY_URL: 'http://gateway:3000',
    INTERNAL_JWT_SECRET,
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
    FCM_DRY_RUN: false,
    FCM_DRY_RUN_TOPIC: 'vietride-e2e-validation',
    SENDGRID_API_KEY: undefined,
    SENDGRID_FROM_EMAIL: undefined,
    SENDGRID_FROM_NAME: 'VietRide',
    NOTIFICATION_RETENTION_DAYS: 90,
    NOTIFICATION_RETENTION_JOB_INTERVAL_MS: 86_400_000,
  };
}
