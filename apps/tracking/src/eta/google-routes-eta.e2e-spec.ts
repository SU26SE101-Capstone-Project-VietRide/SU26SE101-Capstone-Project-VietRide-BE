import { createServer, type Server } from 'node:http';
import { GoogleRoutesEtaProvider } from './google-routes-eta.provider';
import type { Env } from '../config/env.schema';

describe('Google Routes ETA adapter (fake HTTP E2E)', () => {
  let server: Server;
  let baseUrl: string;
  let responseMode: 'success' | 'timeout' | '429' | '500' | 'malformed';

  beforeAll(async () => {
    server = createServer((request, response) => {
      if (request.url !== '/directions/v2:computeRoutes') {
        response.statusCode = 404;
        response.end();
        return;
      }
      if (responseMode === 'timeout') return;
      if (responseMode === '429' || responseMode === '500') {
        response.statusCode = Number(responseMode);
        response.end();
        return;
      }
      response.setHeader('content-type', 'application/json');
      response.end(JSON.stringify(responseMode === 'malformed'
        ? { routes: [{ distanceMeters: -1, duration: 'invalid' }] }
        : { routes: [{ distanceMeters: 12_345, duration: '600s' }] }));
    });
    await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
    const address = server.address();
    if (!address || typeof address === 'string') throw new Error('FAKE_GOOGLE_SERVER_PORT_UNAVAILABLE');
    baseUrl = `http://127.0.0.1:${address.port}`;
  });

  beforeEach(() => {
    responseMode = 'success';
  });

  afterAll(async () => {
    await new Promise<void>((resolve, reject) => server.close((error) => error ? reject(error) : resolve()));
  });

  it('parses fake Google duration and distance without Internet', async () => {
    await expect(createProvider().calculate({
      tripId: '11111111-1111-4111-8111-111111111111',
      latitude: 10,
      longitude: 106,
      recordedAt: '2026-07-31T00:00:00.000Z',
    }, {
      stopId: '22222222-2222-4222-8222-222222222222',
      latitude: 10.1,
      longitude: 106.1,
      sequence: 1,
    })).resolves.toEqual({ distanceMeters: 12_345, etaMinutes: 10 });
  });

  it.each(['429', '500'] as const)('returns null for fake Google %s responses', async (mode) => {
    responseMode = mode;
    await expect(calculate(createProvider())).resolves.toBeNull();
  });

  it('returns null for malformed Google response data', async () => {
    responseMode = 'malformed';
    await expect(calculate(createProvider())).resolves.toBeNull();
  });

  it('aborts a timed-out Google request', async () => {
    responseMode = 'timeout';
    await expect(calculate(createProvider(20))).resolves.toBeNull();
  });

  function createProvider(timeoutMs = 1_000): GoogleRoutesEtaProvider {
    return new GoogleRoutesEtaProvider({
      GOOGLE_ROUTES_BASE_URL: baseUrl,
      GOOGLE_ROUTES_API_KEY: 'fake-key',
      TRACKING_GOOGLE_ROUTES_TIMEOUT_MS: timeoutMs,
    } as Env);
  }
});

function calculate(provider: GoogleRoutesEtaProvider) {
  return provider.calculate({
    tripId: '11111111-1111-4111-8111-111111111111',
    latitude: 10,
    longitude: 106,
    recordedAt: '2026-07-31T00:00:00.000Z',
  }, {
    stopId: '22222222-2222-4222-8222-222222222222',
    latitude: 10.1,
    longitude: 106.1,
    sequence: 1,
  });
}

const realGoogleIt = process.env.RUN_REAL_GOOGLE_E2E === 'true' ? it : it.skip;
realGoogleIt('calls real Google Routes only when RUN_REAL_GOOGLE_E2E=true', async () => {
  const provider = new GoogleRoutesEtaProvider({
    GOOGLE_ROUTES_BASE_URL: process.env.GOOGLE_ROUTES_BASE_URL ?? 'https://routes.googleapis.com',
    GOOGLE_ROUTES_API_KEY: process.env.GOOGLE_ROUTES_API_KEY ?? '',
    TRACKING_GOOGLE_ROUTES_TIMEOUT_MS: 5_000,
  } as Env);
  const result = await provider.calculate({
    tripId: '11111111-1111-4111-8111-111111111111',
    latitude: 10.762622,
    longitude: 106.660172,
    recordedAt: new Date().toISOString(),
  }, {
    stopId: '22222222-2222-4222-8222-222222222222',
    latitude: 10.7769,
    longitude: 106.7009,
    sequence: 1,
  });
  expect(result).toEqual({ distanceMeters: expect.any(Number), etaMinutes: expect.any(Number) });
});
