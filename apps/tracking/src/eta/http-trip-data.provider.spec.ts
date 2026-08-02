import type { TrackingInternalJwtSigner } from '../authorization/tracking-internal-jwt.signer';
import type { Env } from '../config/env.schema';
import { HttpTripDataProvider } from './http-trip-data.provider';

const TRIP_ID = '11111111-1111-4111-8111-111111111111';
const STOP_ID = '22222222-2222-4222-8222-222222222222';

describe('HttpTripDataProvider', () => {
  const originalFetch = global.fetch;

  afterEach(() => {
    global.fetch = originalFetch;
    jest.restoreAllMocks();
  });

  it('accepts the nullable optional fields serialized by the real Trip route-stops endpoint', async () => {
    global.fetch = jest.fn(async () => ({
      ok: true,
      json: async () => ({
        success: true,
        statusCode: 200,
        data: {
          stops: [{
            stopId: STOP_ID,
            latitude: 10.75,
            longitude: 106.67,
            sequence: 1,
            alertRecipientUserIds: null,
            estimatedArrivalTime: null,
          }],
        },
        meta: { traceId: 'trip-e2e', timestamp: '2026-08-01T00:00:00.000Z' },
      }),
    } as Response)) as typeof fetch;
    const signer = { sign: jest.fn(async () => 'internal-token') } as unknown as TrackingInternalJwtSigner;
    const provider = new HttpTripDataProvider({
      TRIP_SERVICE_BASE_URL: 'http://trip.test',
      TRIP_ROUTE_STOPS_PATH: '/internal/v1/trips/:tripId/route-stops',
      TRACKING_DATA_PROVIDER_TIMEOUT_MS: 1_000,
      TRACKING_ROUTE_STOPS_CACHE_TTL_SECONDS: 60,
    } as Env, signer);

    await expect(provider.getRouteStops(TRIP_ID)).resolves.toEqual([{
      stopId: STOP_ID,
      latitude: 10.75,
      longitude: 106.67,
      sequence: 1,
    }]);
  });

  it('refreshes route stops after the 60 second cache TTL', async () => {
    let now = 1_000_000;
    jest.spyOn(Date, 'now').mockImplementation(() => now);
    global.fetch = jest.fn(async () => ({
      ok: true,
      json: async () => ({
        success: true,
        data: {
          stops: [{
            stopId: STOP_ID,
            latitude: 10.75,
            longitude: 106.67,
            sequence: 1,
            status: 'SKIPPED',
          }],
        },
      }),
    } as Response)) as typeof fetch;
    const provider = new HttpTripDataProvider({
      TRIP_SERVICE_BASE_URL: 'http://trip.test',
      TRIP_ROUTE_STOPS_PATH: '/internal/v1/trips/:tripId/route-stops',
      TRACKING_DATA_PROVIDER_TIMEOUT_MS: 1_000,
      TRACKING_ROUTE_STOPS_CACHE_TTL_SECONDS: 60,
    } as Env, {
      sign: jest.fn(async () => 'internal-token'),
    } as unknown as TrackingInternalJwtSigner);

    await expect(provider.getRouteStops(TRIP_ID)).resolves.toEqual([
      expect.objectContaining({ status: 'SKIPPED' }),
    ]);
    now += 59_999;
    await provider.getRouteStops(TRIP_ID);
    expect(global.fetch).toHaveBeenCalledTimes(1);

    now += 2;
    await provider.getRouteStops(TRIP_ID);
    expect(global.fetch).toHaveBeenCalledTimes(2);
  });
});
