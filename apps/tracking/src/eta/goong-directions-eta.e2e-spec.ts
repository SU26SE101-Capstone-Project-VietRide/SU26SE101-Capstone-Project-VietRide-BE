import { createServer, type Server } from 'node:http';
import type { Env } from '../config/env.schema';
import type { GpsUpdateEvent } from '../location/location.service';
import { GoongDirectionsEtaProvider, type RouteCoordinate } from './goong-directions-eta.provider';
import type { EtaProviderResult } from './eta-provider';
import type { TripStopSnapshot } from './trip-data.provider';

type ResponseMode =
  | 'success'
  | 'batch'
  | 'timeout'
  | '401'
  | '403'
  | '429'
  | '500'
  | 'invalid-data'
  | 'malformed-json'
  | 'wrong-count'
  | 'wrong-order';

describe('Goong Directions ETA adapter (fake HTTP E2E)', () => {
  let server: Server;
  let baseUrl: string;
  let responseMode: ResponseMode;
  let receivedRequests: URL[];

  beforeAll(async () => {
    server = createServer((request, response) => {
      const requestUrl = new URL(request.url ?? '/', `http://${request.headers.host}`);
      receivedRequests.push(requestUrl);
      if (request.method !== 'GET' || requestUrl.pathname !== '/Direction') {
        response.statusCode = 404;
        response.end();
        return;
      }
      if (responseMode === 'timeout') return;
      if (['401', '403', '429', '500'].includes(responseMode)) {
        response.statusCode = Number(responseMode);
        response.end();
        return;
      }
      if (responseMode === 'malformed-json') {
        response.setHeader('content-type', 'application/json');
        response.end('{not-json');
        return;
      }

      const origin = parseCoordinate(requestUrl.searchParams.get('origin'));
      const targets = (requestUrl.searchParams.get('destination') ?? '')
        .split(';')
        .filter(Boolean)
        .map(parseCoordinate);
      if (!origin || targets.some((target) => !target)) {
        response.statusCode = 400;
        response.end();
        return;
      }
      const coordinates = targets as RouteCoordinate[];
      const perLegDistance = responseMode === 'batch' ? 1_000 : 12_345;
      const perLegDuration = responseMode === 'batch' ? 60 : 600;
      const legs = coordinates.map((target, index) => ({
        distance: { value: perLegDistance },
        duration: { value: perLegDuration },
        start_location: toGoongCoordinate(index === 0 ? origin : coordinates[index - 1]),
        end_location: toGoongCoordinate(target),
      }));
      if (responseMode === 'wrong-count') legs.pop();
      if (responseMode === 'wrong-order' && legs.length >= 2) {
        const firstEnd = legs[0]?.end_location;
        if (legs[0] && legs[1] && firstEnd) {
          legs[0].end_location = legs[1].end_location;
          legs[1].end_location = firstEnd;
        }
      }
      response.setHeader('content-type', 'application/json');
      response.end(
        JSON.stringify(
          responseMode === 'invalid-data'
            ? {
                routes: [
                  {
                    legs: [
                      {
                        distance: { value: -1 },
                        duration: { value: 'invalid' },
                        start_location: toGoongCoordinate(origin),
                        end_location: toGoongCoordinate(coordinates[0]),
                      },
                    ],
                  },
                ],
              }
            : { routes: [{ legs }] },
        ),
      );
    });
    await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
    const address = server.address();
    if (!address || typeof address === 'string')
      throw new Error('FAKE_GOONG_SERVER_PORT_UNAVAILABLE');
    baseUrl = `http://127.0.0.1:${address.port}`;
  });

  beforeEach(() => {
    responseMode = 'success';
    receivedRequests = [];
  });

  afterAll(async () => {
    await new Promise<void>((resolve, reject) =>
      server.close((error) => (error ? reject(error) : resolve())),
    );
  });

  it('sends the ordered Goong GET query and parses distance and duration', async () => {
    await expect(calculate(createProvider())).resolves.toEqual({
      distanceMeters: 12_345,
      etaMinutes: 10,
    });

    expect(receivedRequests).toHaveLength(1);
    const request = receivedRequests[0];
    expect(request?.searchParams.get('origin')).toBe('10,106');
    expect(request?.searchParams.get('destination')).toBe('10.1,106.1');
    expect(request?.searchParams.get('vehicle')).toBe('car');
    expect(request?.searchParams.get('alternatives')).toBe('false');
    expect(request?.searchParams.get('api_key')).toBe('fake-key');
  });

  it.each(['401', '403', '429', '500'] as const)(
    'returns null for fake Goong %s responses',
    async (mode) => {
      responseMode = mode;
      await expect(calculate(createProvider())).resolves.toBeNull();
    },
  );

  it.each(['invalid-data', 'malformed-json'] as const)(
    'returns null for malformed Goong response mode %s',
    async (mode) => {
      responseMode = mode;
      await expect(calculate(createProvider())).resolves.toBeNull();
    },
  );

  it('aborts a timed-out Goong request', async () => {
    responseMode = 'timeout';
    await expect(calculate(createProvider(20))).resolves.toBeNull();
  });

  it.each(['wrong-count', 'wrong-order'] as const)(
    'rejects the whole batch for %s responses',
    async (mode) => {
      responseMode = mode;
      await expect(
        createProvider().calculateBatch(createGps(), createTargets(2)),
      ).resolves.toBeNull();
    },
  );

  it('chunks ordered targets by configuration and chains each boundary origin', async () => {
    responseMode = 'batch';
    const targets = createTargets(28);

    const result = await createProvider(1_000, 10).calculateBatch(createGps(), targets);

    expect(
      receivedRequests.map((request) => request.searchParams.get('destination')?.split(';').length),
    ).toEqual([10, 10, 8]);
    expect(receivedRequests.map((request) => request.searchParams.get('origin'))).toEqual([
      '10,106',
      `${targets[9]?.latitude},${targets[9]?.longitude}`,
      `${targets[19]?.latitude},${targets[19]?.longitude}`,
    ]);
    expect(result).toHaveLength(28);
    expect(result?.[0]).toEqual({
      targetId: targets[0]?.stopId,
      distanceMeters: 1_000,
      etaMinutes: 1,
    });
    expect(result?.[27]).toEqual({
      targetId: targets[27]?.stopId,
      distanceMeters: 28_000,
      etaMinutes: 568,
    });
  });

  function createProvider(timeoutMs = 1_000, maxDestinations = 10): GoongDirectionsEtaProvider {
    return new GoongDirectionsEtaProvider({
      GOONG_BASE_URL: baseUrl,
      GOONG_API_KEY: 'fake-key',
      GOONG_MAX_DESTINATIONS_PER_REQUEST: maxDestinations,
      TRACKING_ROUTING_TIMEOUT_MS: timeoutMs,
    } as Env);
  }
});

function calculate(provider: GoongDirectionsEtaProvider): Promise<EtaProviderResult | null> {
  const target = createTargets(1)[0];
  if (!target) throw new Error('GOONG_TEST_TARGET_UNAVAILABLE');
  return provider.calculate(createGps(), target);
}

function createGps(): GpsUpdateEvent {
  return {
    tripId: '11111111-1111-4111-8111-111111111111',
    latitude: 10,
    longitude: 106,
    recordedAt: '2026-07-31T00:00:00.000Z',
  };
}

function createTargets(count: number): TripStopSnapshot[] {
  return Array.from({ length: count }, (_, index) => ({
    stopId: `00000000-0000-4000-8000-${String(index + 1).padStart(12, '0')}`,
    latitude: 10.1 + index / 100,
    longitude: 106.1 + index / 100,
    sequence: index + 1,
  }));
}

function parseCoordinate(value: string | null): RouteCoordinate | null {
  if (!value) return null;
  const [latitudeRaw, longitudeRaw] = value.split(',');
  const latitude = Number(latitudeRaw);
  const longitude = Number(longitudeRaw);
  return Number.isFinite(latitude) && Number.isFinite(longitude) ? { latitude, longitude } : null;
}

function toGoongCoordinate(
  coordinate: RouteCoordinate | undefined,
): { lat: number; lng: number } | null {
  return coordinate ? { lat: coordinate.latitude, lng: coordinate.longitude } : null;
}
