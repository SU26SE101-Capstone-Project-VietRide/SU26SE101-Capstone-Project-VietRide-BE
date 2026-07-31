import type { Env } from '../config/env.schema';
import type { TrackingInternalJwtSigner } from '../authorization/tracking-internal-jwt.signer';
import { HttpRouteGeometryProvider } from './http-route-geometry.provider';

const TRIP_ID = '11111111-1111-4111-8111-111111111111';

describe('HttpRouteGeometryProvider', () => {
  const originalFetch = global.fetch;

  afterEach(() => {
    global.fetch = originalFetch;
  });

  it('deduplicates in-flight warming and exposes only completed cache entries to peek', async () => {
    let releaseFetch: () => void = () => undefined;
    global.fetch = jest.fn(() => new Promise<Response>((resolve) => {
      releaseFetch = () => resolve({
        ok: true,
        json: async () => ({
          success: true,
          data: {
            tripId: TRIP_ID,
            points: [{ latitude: 10, longitude: 106 }, { latitude: 10.1, longitude: 106 }],
          },
        }),
      } as Response);
    })) as typeof fetch;
    const signer = { sign: jest.fn(async () => 'internal-token') } as unknown as TrackingInternalJwtSigner;
    const provider = new HttpRouteGeometryProvider({
      TRIP_SERVICE_BASE_URL: 'http://trip.test',
      TRIP_ROUTE_GEOMETRY_PATH: '/internal/v1/trips/:tripId/route-geometry',
      TRACKING_DATA_PROVIDER_TIMEOUT_MS: 1_000,
      TRACKING_ROUTE_GEOMETRY_CACHE_TTL_SECONDS: 600,
    } as Env, signer);

    const first = provider.getRouteGeometry(TRIP_ID);
    const second = provider.getRouteGeometry(TRIP_ID);
    expect(provider.peekCachedRouteGeometry(TRIP_ID)).toBeNull();
    await new Promise<void>((resolve) => setImmediate(resolve));
    expect(global.fetch).toHaveBeenCalledTimes(1);
    releaseFetch();

    await expect(Promise.all([first, second])).resolves.toEqual([
      expect.objectContaining({ tripId: TRIP_ID }),
      expect.objectContaining({ tripId: TRIP_ID }),
    ]);
    expect(provider.peekCachedRouteGeometry(TRIP_ID)).toEqual(expect.objectContaining({ tripId: TRIP_ID }));
  });
});
