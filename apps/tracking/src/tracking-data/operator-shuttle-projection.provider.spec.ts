import type { TrackingInternalJwtSigner } from '../authorization/tracking-internal-jwt.signer';
import type { Env } from '../config/env.schema';
import { OperatorShuttleProjectionProvider } from './operator-shuttle-projection.provider';

describe('OperatorShuttleProjectionProvider', () => {
  afterEach(() => jest.restoreAllMocks());

  it('reads and caches the raw active Shuttle projection for one tenant', async () => {
    const fetchMock = jest.spyOn(global, 'fetch').mockResolvedValue({
      ok: true,
      json: async () => ([{
        shuttleTripId: '11111111-1111-4111-8111-111111111111',
        mainTripId: '22222222-2222-4222-8222-222222222222',
        status: 'IN_PROGRESS',
      }]),
    } as Response);
    const signer = {
      sign: jest.fn(async () => 'internal-jwt'),
    } as unknown as TrackingInternalJwtSigner;
    const provider = new OperatorShuttleProjectionProvider({
      TRIP_SERVICE_BASE_URL: 'http://trip:8080',
      TRACKING_DATA_PROVIDER_TIMEOUT_MS: 1_000,
    } as Env, signer);

    const first = await provider.list('33333333-3333-4333-8333-333333333333');
    const cached = await provider.list('33333333-3333-4333-8333-333333333333');

    expect(first).toEqual(cached);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0]?.[0].toString()).toBe(
      'http://trip:8080/internal/v1/operators/33333333-3333-4333-8333-333333333333/tracking-shuttle-trips',
    );
    expect(fetchMock.mock.calls[0]?.[1]).toEqual(expect.objectContaining({
      headers: { 'X-Internal-Auth': 'Bearer internal-jwt' },
    }));
  });

  it('rejects non-active or malformed downstream projection rows', async () => {
    jest.spyOn(global, 'fetch').mockResolvedValue({
      ok: true,
      json: async () => ([{
        shuttleTripId: '11111111-1111-4111-8111-111111111111',
        mainTripId: '22222222-2222-4222-8222-222222222222',
        status: 'COMPLETED',
      }]),
    } as Response);
    const provider = new OperatorShuttleProjectionProvider({
      TRIP_SERVICE_BASE_URL: 'http://trip:8080',
      TRACKING_DATA_PROVIDER_TIMEOUT_MS: 1_000,
    } as Env, {
      sign: jest.fn(async () => 'internal-jwt'),
    } as unknown as TrackingInternalJwtSigner);

    await expect(provider.list('33333333-3333-4333-8333-333333333333')).rejects.toThrow();
  });
});
