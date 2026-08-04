import { createServer, type IncomingMessage, type Server, type ServerResponse } from 'http';
import { jwtVerify } from 'jose';
import type { Env } from '../config/env.schema';
import { HttpTrackingAuthorizationAdapter } from './http-tracking-authorization.adapter';
import { TrackingInternalJwtSigner } from './tracking-internal-jwt.signer';

const TEST_TRIP_ID = '11111111-1111-4111-8111-111111111111';
const OWNER_USER_ID = '22222222-2222-4222-8222-222222222222';
const PARCEL_RECIPIENT_USER_ID = '33333333-3333-4333-8333-333333333333';
const UNRELATED_USER_ID = '44444444-4444-4444-8444-444444444444';
const DRIVER_USER_ID = '55555555-5555-4555-8555-555555555555';
const INTERNAL_JWT_SECRET = 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa';
const TIMEOUT_MS = 5_000;
const SLOW_RESPONSE_DELAY_MS = TIMEOUT_MS + 500;
const TIMEOUT_TEST_TIMEOUT_MS = SLOW_RESPONSE_DELAY_MS + 2_000;

describe('HttpTrackingAuthorizationAdapter (e2e)', () => {
  let server: Server;
  let baseUrl: string;
  let adapter: HttpTrackingAuthorizationAdapter;

  beforeAll(async () => {
    server = createServer((request, response) => {
      void handleRequest(request, response);
    });
    await listen(server);
    baseUrl = `http://127.0.0.1:${readListeningPort(server)}`;
    const env = createTestEnv(baseUrl);
    adapter = new HttpTrackingAuthorizationAdapter(env, new TrackingInternalJwtSigner(env));
  });

  afterAll(async () => {
    await close(server);
  });

  it('allows passenger booking owner to view trip tracking', async () => {
    const result = await adapter.authorizeTripTracking({ userId: OWNER_USER_ID, role: 'PASSENGER' }, TEST_TRIP_ID);

    expect(result).toEqual({ allowed: true, scope: 'BOOKING_OWNER' });
  });

  it('allows parcel recipient to view trip tracking after booking provider denies', async () => {
    const result = await adapter.authorizeTripTracking(
      { userId: PARCEL_RECIPIENT_USER_ID, role: 'PASSENGER' },
      TEST_TRIP_ID,
    );

    expect(result).toEqual({ allowed: true, scope: 'PARCEL_RECIPIENT' });
  });

  it('denies unrelated passenger access', async () => {
    const result = await adapter.authorizeTripTracking(
      { userId: UNRELATED_USER_ID, role: 'PASSENGER' },
      TEST_TRIP_ID,
    );

    expect(result).toEqual({ allowed: false, error: 'ACCESS_DENIED' });
  });

  it('allows driver assigned to the trip for gps:update authorization', async () => {
    const result = await adapter.authorizeTripTracking({ userId: DRIVER_USER_ID, role: 'DRIVER' }, TEST_TRIP_ID);

    expect(result).toEqual({ allowed: true, scope: 'DRIVER' });
  });

  it('maps downstream timeout to TRACKING_AUTH_UNAVAILABLE', async () => {
    const result = await adapter.authorizeTripTracking({ userId: DRIVER_USER_ID, role: 'DRIVER' }, timeoutTripId());

    expect(result).toEqual({ allowed: false, error: 'TRACKING_AUTH_UNAVAILABLE' });
  }, TIMEOUT_TEST_TIMEOUT_MS);

  async function handleRequest(request: IncomingMessage, response: ServerResponse): Promise<void> {
    const url = new URL(request.url ?? '/', baseUrl);
    await expectInternalJwt(request);

    if (url.pathname.includes(timeoutTripId())) {
      setTimeout(() => response.end(), SLOW_RESPONSE_DELAY_MS);
      return;
    }

    if (url.pathname.endsWith('/tracking-authorization/bookings')) {
      writeJson(response, {
        success: true,
        data: {
          allowed: url.searchParams.get('userId') === OWNER_USER_ID,
          scope: url.searchParams.get('userId') === OWNER_USER_ID ? 'BOOKING_OWNER' : null,
          error: url.searchParams.get('userId') === OWNER_USER_ID ? null : 'ACCESS_DENIED',
        },
      });
      return;
    }

    if (url.pathname.endsWith('/tracking-authorization/parcels')) {
      writeJson(response, {
        allowed: url.searchParams.get('userId') === PARCEL_RECIPIENT_USER_ID,
        scope: 'PARCEL_RECIPIENT',
        error: url.searchParams.get('userId') === PARCEL_RECIPIENT_USER_ID ? undefined : 'ACCESS_DENIED',
      });
      return;
    }

    if (url.pathname.endsWith('/tracking-authorization')) {
      writeJson(response, {
        success: true,
        data: {
          allowed: url.searchParams.get('userId') === DRIVER_USER_ID,
          scope: 'DRIVER',
          error: url.searchParams.get('userId') === DRIVER_USER_ID ? undefined : 'ACCESS_DENIED',
        },
      });
      return;
    }

    response.writeHead(404);
    response.end();
  }
});

async function expectInternalJwt(request: IncomingMessage): Promise<void> {
  const header = request.headers['x-internal-auth'];
  expect(typeof header).toBe('string');
  const token = String(header).slice('Bearer '.length);
  const result = await jwtVerify(token, new TextEncoder().encode(INTERNAL_JWT_SECRET), {
    issuer: 'vietride-gateway',
    audience: 'vietride-internal',
  });
  expect(result.payload.callerService).toBe('tracking');
}

function writeJson(response: ServerResponse, body: unknown): void {
  response.writeHead(200, { 'Content-Type': 'application/json' });
  response.end(JSON.stringify(body));
}

function createTestEnv(baseUrl: string): Env {
  return {
    NODE_ENV: 'test',
    PORT: 3001,
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
    DATABASE_URL: 'postgresql://postgres:postgres@localhost:5432/vietride_tracking',
    LOG_LEVEL: 'info',
    TRIP_SERVICE_BASE_URL: baseUrl,
    BOOKING_SERVICE_BASE_URL: baseUrl,
    PARCEL_SERVICE_BASE_URL: baseUrl,
    TRIP_TRACKING_AUTH_PATH: '/internal/v1/trips/:tripId/tracking-authorization',
    BOOKING_TRACKING_AUTH_PATH: '/internal/v1/trips/:tripId/tracking-authorization/bookings',
    PARCEL_TRACKING_AUTH_PATH: '/internal/v1/trips/:tripId/tracking-authorization/parcels',
    TRACKING_AUTH_HTTP_TIMEOUT_MS: TIMEOUT_MS,
    TRACKING_CORS_ORIGIN: '*',
    TRACKING_SWAGGER_ENABLED: true,
    TRACKING_GPS_FLUSH_ENABLED: false,
    TRACKING_GPS_FLUSH_INTERVAL_MS: 300_000,
    TRACKING_TRIP_DELAY_ENABLED: false,
    TRACKING_TRIP_DELAY_INTERVAL_MS: 300_000,
    TRACKING_OUTBOX_PUBLISH_ENABLED: false,
    TRACKING_OUTBOX_PUBLISH_INTERVAL_MS: 5_000,
    TRACKING_OUTBOX_PUBLISH_BATCH_SIZE: 25,
    TRIP_ROUTE_STOPS_PATH: '/internal/v1/trips/:tripId/route-stops',
    TRIP_ROUTE_GEOMETRY_PATH: '/internal/v1/trips/:tripId/route-geometry',
    BOOKING_PICKUP_BOOKINGS_PATH: '/internal/v1/trips/:tripId/stops/:stopId/pickup-bookings',
    TRACKING_DATA_PROVIDER_TIMEOUT_MS: 2_000,
    TRACKING_ROUTE_STOPS_CACHE_TTL_SECONDS: 300,
    TRACKING_ROUTE_GEOMETRY_CACHE_TTL_SECONDS: 600,
    TRACKING_SHARE_TOKEN_SECRET: 'phase13-test-share-token-secret-32-bytes',
    TRACKING_SHARE_PAGE_URL: 'http://localhost:5173/trip-sharing',
    TRACKING_SHARE_TOKEN_TTL_SECONDS: 86_400,
    TRACKING_SHARE_CONTEXT_RATE_LIMIT_PER_MIN: 60,
    TRACKING_SHARE_SOCKET_RATE_LIMIT_PER_MIN: 20,
    TRACKING_SHARE_SOCKET_REVALIDATE_SECONDS: 60,
    GOOGLE_ROUTES_ENABLED: false,
    GOOGLE_ROUTES_API_KEY: '',
    GOOGLE_ROUTES_BASE_URL: 'https://routes.googleapis.com',
    TRACKING_GOOGLE_ROUTES_TIMEOUT_MS: 1_500,
    TRACKING_ETA_MIN_INTERVAL_SECONDS: 60,
    TRACKING_ETA_CACHE_TTL_SECONDS: 60,
    TRACKING_ETA_FAILURE_COOLDOWN_SECONDS: 300,
  };
}

function timeoutTripId(): string {
  return '66666666-6666-4666-8666-666666666666';
}

function listen(server: Server): Promise<void> {
  return new Promise((resolve) => {
    server.listen(0, '127.0.0.1', resolve);
  });
}

function close(server: Server): Promise<void> {
  return new Promise((resolve, reject) => {
    server.close((error) => {
      if (error) reject(error);
      else resolve();
    });
  });
}

function readListeningPort(server: Server): number {
  const address = server.address();
  if (typeof address === 'object' && address !== null) {
    return address.port;
  }

  throw new Error('AUTH_E2E_PORT_UNAVAILABLE');
}
