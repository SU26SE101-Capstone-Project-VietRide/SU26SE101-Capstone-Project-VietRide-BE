import type { Env } from '../config/env.schema';
import type { TrackingInternalJwtSigner } from '../authorization/tracking-internal-jwt.signer';
import { OperatorTripProjectionProvider } from './operator-trip-projection.provider';

describe('OperatorTripProjectionProvider', () => {
  afterEach(() => jest.restoreAllMocks());

  it('reads the ADR 0004 response envelope and caches the tenant projection', async () => {
    const fetchMock = jest.spyOn(global, 'fetch').mockResolvedValue({
      ok: true,
      json: async () => ({
        success: true,
        statusCode: 200,
        data: [{
          tripId: '11111111-1111-4111-8111-111111111111',
          status: 'IN_PROGRESS',
        }],
        meta: { traceId: 'trace-1' },
      }),
    } as Response);
    const env = {
      TRIP_SERVICE_BASE_URL: 'http://trip:8080',
      TRACKING_DATA_PROVIDER_TIMEOUT_MS: 1_000,
    } as Env;
    const signer = {
      sign: jest.fn(async () => 'internal-jwt'),
    } as unknown as TrackingInternalJwtSigner;
    const provider = new OperatorTripProjectionProvider(env, signer);

    const first = await provider.list('22222222-2222-4222-8222-222222222222', 'IN_PROGRESS');
    const cached = await provider.list('22222222-2222-4222-8222-222222222222', 'IN_PROGRESS');

    expect(first).toEqual(cached);
    expect(first).toHaveLength(1);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0]?.[0].toString()).toContain('status=IN_PROGRESS');
  });

  it('accepts the raw array returned by the internal Trip projection endpoint', async () => {
    jest.spyOn(global, 'fetch').mockResolvedValue({
      ok: true,
      json: async () => ([{
        tripId: '11111111-1111-4111-8111-111111111111',
        status: 'IN_PROGRESS',
      }]),
    } as Response);
    const provider = new OperatorTripProjectionProvider({
      TRIP_SERVICE_BASE_URL: 'http://trip:8080',
      TRACKING_DATA_PROVIDER_TIMEOUT_MS: 1_000,
    } as Env, {
      sign: jest.fn(async () => 'internal-jwt'),
    } as unknown as TrackingInternalJwtSigner);

    await expect(provider.list('22222222-2222-4222-8222-222222222222')).resolves.toEqual([{
      tripId: '11111111-1111-4111-8111-111111111111',
      status: 'IN_PROGRESS',
    }]);
  });
});
