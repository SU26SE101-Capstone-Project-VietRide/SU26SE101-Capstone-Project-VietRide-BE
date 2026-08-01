import { createServer, type Server } from 'node:http';
import { RedisService } from '@vietride/nest-redis';
import type { Env } from '../config/env.schema';
import { GoogleRoutesEtaProvider } from '../eta/google-routes-eta.provider';
import { ShuttleEtaService } from './shuttle-eta.service';

const SHUTTLE_ID = '36000000-0000-4000-8000-000000000001';

describe('Shuttle Google Routes ETA (fake HTTP E2E)', () => {
  let server: Server;
  let baseUrl: string;
  let receivedApiKey: string | undefined;
  let receivedFieldMask: string | undefined;
  let receivedBody: unknown;

  beforeAll(async () => {
    server = createServer((request, response) => {
      const chunks: Buffer[] = [];
      request.on('data', (chunk: Buffer) => chunks.push(chunk));
      request.on('end', () => {
        receivedApiKey = readHeader(request.headers['x-goog-api-key']);
        receivedFieldMask = readHeader(request.headers['x-goog-fieldmask']);
        receivedBody = JSON.parse(Buffer.concat(chunks).toString('utf8')) as unknown;
        response.setHeader('content-type', 'application/json');
        response.end(JSON.stringify({ routes: [{ distanceMeters: 5_909, duration: '1019s' }] }));
      });
    });
    await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
    const address = server.address();
    if (!address || typeof address === 'string') throw new Error('FAKE_GOOGLE_SERVER_PORT_UNAVAILABLE');
    baseUrl = `http://127.0.0.1:${address.port}`;
  });

  beforeEach(() => {
    receivedApiKey = undefined;
    receivedFieldMask = undefined;
    receivedBody = undefined;
  });

  afterAll(async () => {
    await new Promise<void>((resolve, reject) =>
      server.close((error) => (error ? reject(error) : resolve())),
    );
  });

  it('uses the shared Google provider for Shuttle ETA without changing the public event', async () => {
    const env = createEnv(baseUrl, 'fake-shuttle-key');
    const service = createService(env);

    await expect(service.handleGpsUpdate(createGps(), createContext())).resolves.toEqual({
      shuttleTripId: SHUTTLE_ID,
      nextPickupOrder: 1,
      etaMinutes: 17,
      estimatedArrivalTime: '2026-08-01T01:17:00.000Z',
      distanceMeters: 5_909,
      updatedAt: expect.any(String),
    });
    expect(receivedApiKey).toBe('fake-shuttle-key');
    expect(receivedFieldMask).toBe('routes.duration,routes.distanceMeters');
    expect(receivedBody).toEqual(expect.objectContaining({
      origin: { location: { latLng: { latitude: 10.762622, longitude: 106.660172 } } },
      destination: { location: { latLng: { latitude: 10.7769, longitude: 106.7009 } } },
      travelMode: 'DRIVE',
      routingPreference: 'TRAFFIC_AWARE',
    }));
  });
});

const realGoogleIt = process.env.RUN_REAL_GOOGLE_E2E === 'true' ? it : it.skip;
realGoogleIt('calculates Shuttle ETA with the real Google Routes API', async () => {
  const env = createEnv(
    process.env.GOOGLE_ROUTES_BASE_URL ?? 'https://routes.googleapis.com',
    process.env.GOOGLE_ROUTES_API_KEY ?? '',
  );
  const provider = new GoogleRoutesEtaProvider(env);
  await expect(provider.calculateCoordinates(
    { latitude: 10.762622, longitude: 106.660172 },
    { latitude: 10.7769, longitude: 106.7009 },
  )).resolves.toEqual({
    distanceMeters: expect.any(Number),
    etaMinutes: expect.any(Number),
  });
  const event = await createService(env, provider).handleGpsUpdate(createGps(), createContext());

  expect(event).toEqual(expect.objectContaining({
    shuttleTripId: SHUTTLE_ID,
    nextPickupOrder: 1,
    etaMinutes: expect.any(Number),
    distanceMeters: expect.any(Number),
  }));
});

function createService(
  env: Env,
  provider = new GoogleRoutesEtaProvider(env),
): ShuttleEtaService {
  const store = new Map<string, string>();
  const multi = createMulti(store);
  const redis = {
    getClient: () => ({
      get: async (key: string) => store.get(key) ?? null,
      set: async (key: string, value: string) => {
        store.set(key, value);
        return 'OK';
      },
      eval: async () => 1,
      multi: () => multi,
    }),
  } as unknown as RedisService;
  return new ShuttleEtaService(redis, provider, env);
}

function createEnv(baseUrl: string, apiKey: string): Env {
  return {
    GOOGLE_ROUTES_ENABLED: true,
    GOOGLE_ROUTES_API_KEY: apiKey,
    GOOGLE_ROUTES_BASE_URL: baseUrl,
    TRACKING_GOOGLE_ROUTES_TIMEOUT_MS: 5_000,
    TRACKING_ETA_MIN_INTERVAL_SECONDS: 60,
    TRACKING_ETA_CACHE_TTL_SECONDS: 60,
    TRACKING_ETA_FAILURE_COOLDOWN_SECONDS: 300,
  } as Env;
}

function createGps() {
  return {
    shuttleTripId: SHUTTLE_ID,
    latitude: 10.762622,
    longitude: 106.660172,
    speedKmh: 30,
    recordedAt: '2026-08-01T01:00:00.000Z',
  };
}

function createContext() {
  return {
    shuttleTripId: SHUTTLE_ID,
    mainTripId: '36000000-0000-4000-8000-000000000002',
    operatorId: '36000000-0000-4000-8000-000000000003',
    driverUserId: '36000000-0000-4000-8000-000000000004',
    allowed: true,
    scope: 'DRIVER',
    stops: [{
      pickupOrder: 1,
      bookingId: '36000000-0000-4000-8000-000000000005',
      latitude: 10.7769,
      longitude: 106.7009,
      status: 'PENDING',
      isStation: false,
    }],
  };
}

interface MultiMock {
  set: jest.Mock;
  exec: jest.Mock;
}

function createMulti(store: Map<string, string>): MultiMock {
  const pending: Array<[string, string]> = [];
  const multi = {} as MultiMock;
  multi.set = jest.fn((key: string, value: string): MultiMock => {
    pending.push([key, value]);
    return multi;
  });
  multi.exec = jest.fn(async () => {
    for (const [key, value] of pending.splice(0)) store.set(key, value);
    return [];
  });
  return multi;
}

function readHeader(value: string | string[] | undefined): string | undefined {
  return Array.isArray(value) ? value[0] : value;
}
