import { createServer, type Server } from 'node:http';
import { RedisService } from '@vietride/nest-redis';
import type { Env } from '../config/env.schema';
import { GoongDirectionsEtaProvider } from '../eta/goong-directions-eta.provider';
import type { ShuttleGpsUpdateDto } from './shuttle.dto';
import { ShuttleEtaService } from './shuttle-eta.service';
import type { ShuttleTrackingContext } from './shuttle.service';

const SHUTTLE_ID = '36000000-0000-4000-8000-000000000001';

describe('Shuttle Goong Directions ETA (fake HTTP E2E)', () => {
  let server: Server;
  let baseUrl: string;
  let receivedRequest: URL | undefined;

  beforeAll(async () => {
    server = createServer((request, response) => {
      const requestUrl = new URL(request.url ?? '/', `http://${request.headers.host}`);
      receivedRequest = requestUrl;
      response.setHeader('content-type', 'application/json');
      response.end(
        JSON.stringify({
          routes: [
            {
              legs: [
                {
                  distance: { value: 5_909 },
                  duration: { value: 1_019 },
                  start_location: { lat: 10.762622, lng: 106.660172 },
                  end_location: { lat: 10.7769, lng: 106.7009 },
                },
              ],
            },
          ],
        }),
      );
    });
    await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
    const address = server.address();
    if (!address || typeof address === 'string')
      throw new Error('FAKE_GOONG_SERVER_PORT_UNAVAILABLE');
    baseUrl = `http://127.0.0.1:${address.port}`;
  });

  beforeEach(() => {
    receivedRequest = undefined;
  });

  afterAll(async () => {
    await new Promise<void>((resolve, reject) =>
      server.close((error) => (error ? reject(error) : resolve())),
    );
  });

  it('uses the shared Goong provider for Shuttle ETA without changing the public event', async () => {
    const env = createEnv(baseUrl, 'fake-shuttle-key');
    const service = createService(env);

    await expect(service.handleGpsUpdate(createGps(), createContext())).resolves.toEqual({
      shuttleTripId: SHUTTLE_ID,
      nextPickupOrder: 1,
      etaMinutes: 17,
      estimatedArrivalTime: '2026-08-01T01:17:00.000Z',
      distanceMeters: 5_909,
      updatedAt: expect.any(String) as unknown,
    });
    expect(receivedRequest?.pathname).toBe('/v2/direction');
    expect(receivedRequest?.searchParams.get('origin')).toBe('10.762622,106.660172');
    expect(receivedRequest?.searchParams.get('destination')).toBe('10.7769,106.7009');
    expect(receivedRequest?.searchParams.get('vehicle')).toBe('car');
    expect(receivedRequest?.searchParams.get('alternatives')).toBe('false');
    expect(receivedRequest?.searchParams.get('api_key')).toBe('fake-shuttle-key');
  });
});

function createService(
  env: Env,
  provider = new GoongDirectionsEtaProvider(env),
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
    ROUTING_PROVIDER: 'GOONG',
    GOONG_API_KEY: apiKey,
    GOONG_BASE_URL: baseUrl,
    GOONG_MAX_DESTINATIONS_PER_REQUEST: 10,
    TRACKING_ROUTING_TIMEOUT_MS: 5_000,
    TRACKING_ETA_MIN_INTERVAL_SECONDS: 60,
    TRACKING_ETA_CACHE_TTL_SECONDS: 60,
    TRACKING_ETA_FAILURE_COOLDOWN_SECONDS: 300,
  } as Env;
}

function createGps(): ShuttleGpsUpdateDto {
  return {
    shuttleTripId: SHUTTLE_ID,
    latitude: 10.762622,
    longitude: 106.660172,
    speedKmh: 30,
    recordedAt: '2026-08-01T01:00:00.000Z',
  };
}

function createContext(): ShuttleTrackingContext {
  return {
    shuttleTripId: SHUTTLE_ID,
    mainTripId: '36000000-0000-4000-8000-000000000002',
    operatorId: '36000000-0000-4000-8000-000000000003',
    driverUserId: '36000000-0000-4000-8000-000000000004',
    allowed: true,
    scope: 'DRIVER' as const,
    stops: [
      {
        pickupOrder: 1,
        bookingId: '36000000-0000-4000-8000-000000000005',
        latitude: 10.7769,
        longitude: 106.7009,
        status: 'PENDING',
        isStation: false,
      },
    ],
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
