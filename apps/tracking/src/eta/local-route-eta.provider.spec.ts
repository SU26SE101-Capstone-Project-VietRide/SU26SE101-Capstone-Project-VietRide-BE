import { LocalRouteEtaProvider } from './local-route-eta.provider';
import type { RouteGeometryProvider } from '../off-route/route-geometry.provider';

describe('LocalRouteEtaProvider', () => {
  it('uses cumulative polyline distance instead of direct Haversine distance', async () => {
    const routeProvider: RouteGeometryProvider = {
      peekCachedRouteGeometry: () => ({
        tripId: '11111111-1111-4111-8111-111111111111',
        points: [
          { latitude: 10, longitude: 106 },
          { latitude: 10.1, longitude: 106 },
          { latitude: 10.1, longitude: 106.1 },
        ],
      }),
      getRouteGeometry: async () => null,
    };
    const provider = new LocalRouteEtaProvider(routeProvider);
    const result = await provider.calculate({
      tripId: '11111111-1111-4111-8111-111111111111',
      latitude: 10,
      longitude: 106,
      speedKmh: 60,
      recordedAt: '2026-07-31T00:00:00.000Z',
    }, {
      stopId: '22222222-2222-4222-8222-222222222222',
      latitude: 10.1,
      longitude: 106.1,
      sequence: 1,
    });
    expect(result?.distanceMeters).toBeGreaterThan(20_000);
    expect(result?.etaMinutes).toBeGreaterThanOrEqual(20);
  });
});
