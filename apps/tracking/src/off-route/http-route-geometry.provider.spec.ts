import type { Env } from '../config/env.schema';
import type { TrackingInternalJwtSigner } from '../authorization/tracking-internal-jwt.signer';
import { HttpRouteGeometryProvider } from './http-route-geometry.provider';

const TRIP_ID = '11111111-1111-4111-8111-111111111111';

describe('HttpRouteGeometryProvider', () => {
  const originalFetch = global.fetch;
  let now = 1_000_000;

  afterEach(() => {
    global.fetch = originalFetch;
    jest.restoreAllMocks();
  });

  beforeEach(() => {
    now = 1_000_000;
    jest.spyOn(Date, 'now').mockImplementation(() => now);
  });

  it('deduplicates detailed in-flight requests and keeps legacy access to usable snapshots', async () => {
    let releaseFetch: () => void = () => undefined;
    global.fetch = jest.fn(() => new Promise<Response>((resolve) => {
      releaseFetch = () => resolve(jsonResponse(200, validEnvelope()));
    })) as typeof fetch;
    const provider = createProvider();

    const first = provider.getDetailedRouteGeometry(TRIP_ID);
    const second = provider.getDetailedRouteGeometry(TRIP_ID);
    expect(provider.peekCachedRouteGeometry(TRIP_ID)).toBeNull();
    await new Promise<void>((resolve) => setImmediate(resolve));
    expect(global.fetch).toHaveBeenCalledTimes(1);
    releaseFetch();

    await expect(Promise.all([first, second])).resolves.toEqual([
      expect.objectContaining({ kind: 'ok' }),
      expect.objectContaining({ kind: 'ok' }),
    ]);
    expect(provider.peekCachedRouteGeometry(TRIP_ID)).toEqual(expect.objectContaining({ tripId: TRIP_ID }));
    await expect(provider.getRouteGeometry(TRIP_ID)).resolves.toEqual(
      expect.objectContaining({ tripId: TRIP_ID }),
    );
  });

  it.each([
    [404, 'not_found'],
    [401, 'unavailable'],
    [403, 'unavailable'],
    [500, 'unavailable'],
  ] as const)('maps internal HTTP %s to %s and caches it for 30 seconds', async (status, kind) => {
    global.fetch = jest.fn(async () => jsonResponse(status, {})) as typeof fetch;
    const provider = createProvider();

    await expect(provider.getDetailedRouteGeometry(TRIP_ID)).resolves.toEqual({ kind });
    await expect(provider.getDetailedRouteGeometry(TRIP_ID)).resolves.toEqual({ kind });
    expect(global.fetch).toHaveBeenCalledTimes(1);

    now += 30_001;
    await provider.getDetailedRouteGeometry(TRIP_ID);
    expect(global.fetch).toHaveBeenCalledTimes(2);
  });

  it('treats malformed envelopes and mismatched trip IDs as unavailable', async () => {
    global.fetch = jest
      .fn()
      .mockResolvedValueOnce(jsonResponse(200, { success: true, data: { points: [] } }))
      .mockResolvedValueOnce(jsonResponse(200, validEnvelope({
        tripId: '22222222-2222-4222-8222-222222222222',
      }))) as typeof fetch;
    const provider = createProvider();

    await expect(provider.getDetailedRouteGeometry(TRIP_ID)).resolves.toEqual({ kind: 'unavailable' });
    now += 30_001;
    await expect(provider.getDetailedRouteGeometry(TRIP_ID)).resolves.toEqual({ kind: 'unavailable' });
  });

  it('keeps STOPS_ONLY detailed context while hiding unusable geometry from legacy consumers', async () => {
    global.fetch = jest.fn(async () => jsonResponse(200, validEnvelope({
      geometrySource: 'STOPS_ONLY',
      points: [{ latitude: 10, longitude: 106 }],
    }))) as typeof fetch;
    const provider = createProvider();

    await expect(provider.getDetailedRouteGeometry(TRIP_ID)).resolves.toEqual(
      expect.objectContaining({ kind: 'ok' }),
    );
    expect(provider.peekCachedRouteGeometry(TRIP_ID)).toBeNull();
    await expect(provider.getRouteGeometry(TRIP_ID)).resolves.toBeNull();
    expect(global.fetch).toHaveBeenCalledTimes(1);
  });

  it('keeps two-point STOPS_ONLY geometry available to legacy ETA and off-route consumers', async () => {
    global.fetch = jest.fn(async () => jsonResponse(200, validEnvelope({
      geometrySource: 'STOPS_ONLY',
    }))) as typeof fetch;
    const provider = createProvider();

    await expect(provider.getRouteGeometry(TRIP_ID)).resolves.toEqual(
      expect.objectContaining({ geometrySource: 'STOPS_ONLY' }),
    );
    expect(provider.peekCachedRouteGeometry(TRIP_ID)).not.toBeNull();
  });

  it('refreshes two-point STOPS_ONLY geometry after the 30-second fallback TTL', async () => {
    global.fetch = jest.fn(async () => jsonResponse(200, validEnvelope({
      geometrySource: 'STOPS_ONLY',
    }))) as typeof fetch;
    const provider = createProvider();

    await provider.getDetailedRouteGeometry(TRIP_ID);
    now += 29_999;
    await provider.getDetailedRouteGeometry(TRIP_ID);
    expect(global.fetch).toHaveBeenCalledTimes(1);

    now += 2;
    await provider.getDetailedRouteGeometry(TRIP_ID);
    expect(global.fetch).toHaveBeenCalledTimes(2);
  });

  it('uses the configured positive cache TTL', async () => {
    global.fetch = jest.fn(async () => jsonResponse(200, validEnvelope())) as typeof fetch;
    const provider = createProvider();

    await provider.getDetailedRouteGeometry(TRIP_ID);
    now += 599_999;
    await provider.getDetailedRouteGeometry(TRIP_ID);
    expect(global.fetch).toHaveBeenCalledTimes(1);

    now += 2;
    await provider.getDetailedRouteGeometry(TRIP_ID);
    expect(global.fetch).toHaveBeenCalledTimes(2);
  });
});

function createProvider(): HttpRouteGeometryProvider {
  const signer = { sign: jest.fn(async () => 'internal-token') } as unknown as TrackingInternalJwtSigner;
  return new HttpRouteGeometryProvider({
    TRIP_SERVICE_BASE_URL: 'http://trip.test',
    TRIP_ROUTE_GEOMETRY_PATH: '/internal/v1/trips/:tripId/route-geometry',
    TRACKING_DATA_PROVIDER_TIMEOUT_MS: 1_000,
    TRACKING_ROUTE_GEOMETRY_CACHE_TTL_SECONDS: 600,
  } as Env, signer);
}

function validEnvelope(overrides: Record<string, unknown> = {}): unknown {
  return {
    success: true,
    statusCode: 200,
    data: {
      tripId: TRIP_ID,
      geometrySource: 'ROUTE_POLYLINE',
      points: [{ latitude: 10, longitude: 106 }, { latitude: 10.1, longitude: 106.1 }],
      originStation: {
        stationId: '33333333-3333-4333-8333-333333333333',
        name: 'Origin',
        latitude: 10,
        longitude: 106,
      },
      intermediateStops: [],
      destinationStation: {
        stationId: '44444444-4444-4444-8444-444444444444',
        name: 'Destination',
        latitude: 10.1,
        longitude: 106.1,
      },
      alertRecipientUserIds: null,
      ...overrides,
    },
    meta: { traceId: 'trip-e2e', timestamp: '2026-08-01T00:00:00.000Z' },
  };
}

function jsonResponse(status: number, body: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
  } as Response;
}
