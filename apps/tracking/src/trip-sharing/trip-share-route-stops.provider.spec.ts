import { HttpStatus } from '@nestjs/common';
import type { TrackingInternalJwtSigner } from '../authorization/tracking-internal-jwt.signer';
import type { Env } from '../config/env.schema';
import { TripShareRouteStopsProvider } from './trip-share-route-stops.provider';

const TRIP_ID = '11111111-1111-4111-8111-111111111111';
const STOP_ID = '22222222-2222-4222-8222-222222222222';
const INTERNAL_TOKEN = 'internal-token';

describe('TripShareRouteStopsProvider', () => {
  const originalFetch = global.fetch;

  afterEach(() => {
    global.fetch = originalFetch;
    jest.restoreAllMocks();
    jest.useRealTimers();
  });

  it('maps a valid non-empty ApiResponse and sends internal authentication', async () => {
    global.fetch = jest.fn(async () => response({
      success: true,
      statusCode: 200,
      data: {
        stops: [{
          stopId: STOP_ID,
          latitude: 10.75,
          longitude: 106.67,
          sequence: 1,
          status: 'PENDING',
          alertRecipientUserIds: null,
          estimatedArrivalTime: null,
        }],
      },
      meta: { traceId: 'trip-test', timestamp: '2026-08-03T00:00:00.000Z' },
    })) as typeof fetch;

    const provider = createProvider();

    await expect(provider.getRouteStops(TRIP_ID)).resolves.toEqual([{
      stopId: STOP_ID,
      latitude: 10.75,
      longitude: 106.67,
      sequence: 1,
      status: 'PENDING',
    }]);
    expect(global.fetch).toHaveBeenCalledWith(
      `http://trip.test/internal/v1/trips/${TRIP_ID}/route-stops`,
      expect.objectContaining({
        method: 'GET',
        headers: { 'X-Internal-Auth': `Bearer ${INTERNAL_TOKEN}` },
        signal: expect.any(AbortSignal),
      }),
    );
  });

  it('accepts a valid empty stops array', async () => {
    global.fetch = jest.fn(async () => response({ success: true, data: { stops: [] } })) as typeof fetch;

    await expect(createProvider().getRouteStops(TRIP_ID)).resolves.toEqual([]);
  });

  it.each([
    ['non-2xx response', () => response({}, false)],
    ['malformed envelope', () => response({ success: false, data: { stops: [] } })],
    ['malformed data', () => response({ success: true, data: { stops: 'invalid' } })],
    ['malformed JSON', () => ({
      ok: true,
      json: async () => { throw new SyntaxError('invalid JSON'); },
    } as unknown as Response)],
  ])('fails closed with 503 for a %s', async (_caseName, createResponse) => {
    global.fetch = jest.fn(async () => createResponse()) as typeof fetch;

    await expectUnavailable(createProvider().getRouteStops(TRIP_ID));
  });

  it('maps a network failure to 503', async () => {
    global.fetch = jest.fn(async () => { throw new Error('network unavailable'); }) as typeof fetch;

    await expectUnavailable(createProvider().getRouteStops(TRIP_ID));
  });

  it('maps a timeout to 503', async () => {
    jest.useFakeTimers();
    global.fetch = jest.fn((_input, init) => new Promise<Response>((_resolve, reject) => {
      init?.signal?.addEventListener('abort', () => reject(new Error('aborted')));
    })) as typeof fetch;
    const pending = createProvider(10).getRouteStops(TRIP_ID);
    const assertion = expectUnavailable(pending);

    await jest.advanceTimersByTimeAsync(10);

    await assertion;
  });

  it('maps an internal JWT signing failure to 503', async () => {
    global.fetch = jest.fn() as typeof fetch;
    const signer = {
      sign: jest.fn(async () => { throw new Error('signing unavailable'); }),
    } as unknown as TrackingInternalJwtSigner;

    await expectUnavailable(createProvider(1_000, signer).getRouteStops(TRIP_ID));
    expect(global.fetch).not.toHaveBeenCalled();
  });
});

function createProvider(
  timeoutMs = 1_000,
  signer = { sign: jest.fn(async () => INTERNAL_TOKEN) } as unknown as TrackingInternalJwtSigner,
): TripShareRouteStopsProvider {
  return new TripShareRouteStopsProvider({
    TRIP_SERVICE_BASE_URL: 'http://trip.test',
    TRIP_ROUTE_STOPS_PATH: '/internal/v1/trips/:tripId/route-stops',
    TRACKING_DATA_PROVIDER_TIMEOUT_MS: timeoutMs,
  } as Env, signer);
}

function response(body: unknown, ok = true): Response {
  return { ok, json: async () => body } as Response;
}

async function expectUnavailable(promise: Promise<unknown>): Promise<void> {
  await expect(promise).rejects.toMatchObject({
    status: HttpStatus.SERVICE_UNAVAILABLE,
    response: { errorCode: 'TRACKING_TRIP_UNAVAILABLE' },
  });
}
