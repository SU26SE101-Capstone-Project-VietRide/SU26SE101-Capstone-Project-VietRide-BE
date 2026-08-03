import {
  GoneException,
  HttpException,
  HttpStatus,
  INestApplication,
  ServiceUnavailableException,
  UnauthorizedException,
} from '@nestjs/common';
import { APP_FILTER, APP_INTERCEPTOR } from '@nestjs/core';
import { DocumentBuilder, SwaggerModule } from '@nestjs/swagger';
import { Test } from '@nestjs/testing';
import {
  ApiResponseExceptionFilter,
  ApiResponseInterceptor,
  NestCommonModule,
} from '@vietride/nest-common';
import type { AddressInfo } from 'node:net';
import { ROUTE_GEOMETRY_PROVIDER } from '../off-route/off-route.constants';
import {
  TripShareAccessService,
  type TripShareAccessContext,
} from './trip-share-access.service';
import { TripShareContextService } from './trip-share-context.service';
import { TripSharePublicController } from './trip-share-public.controller';
import { TripShareRouteStopsProvider } from './trip-share-route-stops.provider';
import { TripShareTokenGuard } from './trip-share-token.guard';
import { TripShareTrackingStateRepository } from './trip-share-tracking-state.repository';

const TRIP_ID = '11111111-1111-4111-8111-111111111111';
const GRANT_ID = '22222222-2222-4222-8222-222222222222';
const ORIGIN_ID = '44444444-4444-4444-8444-444444444444';
const DESTINATION_ID = '55555555-5555-4555-8555-555555555555';
const STOP_ID = '66666666-6666-4666-8666-666666666666';
const ACTIVE_EXPIRY = new Date('2099-08-04T10:00:00.000Z');
const VALID_TOKEN = 'valid-share-token';
const ACCESS: TripShareAccessContext = {
  grantId: GRANT_ID,
  tripId: TRIP_ID,
  expiresAt: ACTIVE_EXPIRY,
  status: 'IN_PROGRESS',
};

interface Envelope<T> {
  success: boolean;
  statusCode: number;
  data?: T;
  error?: { code: string; message: string };
  meta?: { traceId: string; timestamp: string };
}

describe('TripSharePublicController (e2e)', () => {
  let app: INestApplication;
  let port: number;
  const access = { authorize: jest.fn() };
  const routes = { getDetailedRouteGeometry: jest.fn() };
  const routeStops = { getRouteStops: jest.fn() };
  const state = { findLatest: jest.fn(), findEta: jest.fn() };

  beforeAll(async () => {
    const moduleRef = await Test.createTestingModule({
      imports: [NestCommonModule],
      controllers: [TripSharePublicController],
      providers: [
        TripShareContextService,
        TripShareTokenGuard,
        { provide: TripShareAccessService, useValue: access },
        { provide: TripShareTrackingStateRepository, useValue: state },
        { provide: ROUTE_GEOMETRY_PROVIDER, useValue: routes },
        { provide: TripShareRouteStopsProvider, useValue: routeStops },
        { provide: APP_FILTER, useValue: new ApiResponseExceptionFilter() },
        { provide: APP_INTERCEPTOR, useValue: new ApiResponseInterceptor() },
      ],
    }).compile();

    app = moduleRef.createNestApplication();
    await app.listen(0);
    port = (app.getHttpServer().address() as AddressInfo).port;
  });

  beforeEach(() => {
    jest.clearAllMocks();
    access.authorize.mockImplementation(async (rawToken?: string) => {
      if (rawToken === VALID_TOKEN) return ACCESS;
      throw new UnauthorizedException({
        errorCode: 'TRACKING_SHARE_TOKEN_INVALID',
        detail: 'The trip share token is invalid',
      });
    });
    routes.getDetailedRouteGeometry.mockResolvedValue({ kind: 'ok', snapshot: createRoute() });
    routeStops.getRouteStops.mockResolvedValue([
      { stopId: STOP_ID, sequence: 1, status: 'PENDING', latitude: 10.1, longitude: 106.1 },
    ]);
    state.findLatest.mockResolvedValue({
      tripId: TRIP_ID,
      latitude: 10.05,
      longitude: 106.05,
      recordedAt: '2026-08-03T10:01:00.000Z',
    });
    state.findEta.mockResolvedValue({
      tripId: TRIP_ID,
      stopId: STOP_ID,
      etaMinutes: 12,
      estimatedArrivalTime: '2026-08-03T10:20:00.000Z',
      distanceMeters: 8_500,
      updatedAt: '2026-08-03T10:02:00.000Z',
    });
  });

  afterAll(async () => {
    if (app) await app.close();
  });

  it('returns a no-store ADR0004 envelope with only the anonymous allow-list', async () => {
    const response = await getContext(VALID_TOKEN);
    expect(response.status).toBe(200);
    assertNoStoreHeaders(response.headers);
    expect(response.body).toMatchObject({
      success: true,
      statusCode: 200,
      data: {
        status: 'IN_PROGRESS',
        route: {
          originName: 'Origin',
          destinationName: 'Destination',
          geometry: { type: 'LineString', coordinates: [[106, 10], [106.2, 10.2]] },
        },
      },
      meta: { traceId: expect.any(String), timestamp: expect.any(String) },
    });
    const json = JSON.stringify(response.body);
    for (const forbidden of [TRIP_ID, GRANT_ID, ORIGIN_ID, DESTINATION_ID, STOP_ID, 'tripId', 'stopId']) {
      expect(json).not.toContain(forbidden);
    }
  });

  it('returns indistinguishable 401 envelopes for missing and invalid share tokens', async () => {
    const missing = await getContext();
    const invalid = await getContext('v1.invalid.invalid');
    expect([missing.status, invalid.status]).toEqual([401, 401]);
    expect([missing.body.error?.code, invalid.body.error?.code]).toEqual([
      'TRACKING_SHARE_TOKEN_INVALID',
      'TRACKING_SHARE_TOKEN_INVALID',
    ]);
    assertNoStoreHeaders(missing.headers);
    assertNoStoreHeaders(invalid.headers);
  });

  it('preserves no-store headers on a representative 410 guard failure', async () => {
    access.authorize.mockRejectedValueOnce(new GoneException({
      errorCode: 'TRACKING_SHARE_LINK_UNAVAILABLE',
      detail: 'The trip share link is no longer available',
    }));

    const response = await getContext('unavailable-share-token');

    expect(response.status).toBe(410);
    expect(response.body.error?.code).toBe('TRACKING_SHARE_LINK_UNAVAILABLE');
    assertNoStoreHeaders(response.headers);
  });

  it('preserves no-store headers on a representative 429 guard failure', async () => {
    access.authorize.mockRejectedValueOnce(new HttpException(
      { errorCode: 'RATE_LIMITED', detail: 'Trip sharing rate limit exceeded' },
      HttpStatus.TOO_MANY_REQUESTS,
    ));

    const response = await getContext('rate-limited-share-token');

    expect(response.status).toBe(429);
    expect(response.body.error?.code).toBe('RATE_LIMITED');
    assertNoStoreHeaders(response.headers);
  });

  it('preserves no-store headers on a representative 503 guard failure', async () => {
    access.authorize.mockRejectedValueOnce(new ServiceUnavailableException({
      errorCode: 'TRACKING_SHARE_RATE_LIMIT_UNAVAILABLE',
      detail: 'Trip sharing rate limiting is unavailable',
    }));

    const response = await getContext('rate-limit-unavailable-share-token');

    expect(response.status).toBe(503);
    expect(response.body.error?.code).toBe('TRACKING_SHARE_RATE_LIMIT_UNAVAILABLE');
    assertNoStoreHeaders(response.headers);
  });

  it('returns null dynamic fields when GPS, route polyline and ETA are absent', async () => {
    routes.getDetailedRouteGeometry.mockResolvedValueOnce({
      kind: 'ok',
      snapshot: createRoute({ geometrySource: 'STOPS_ONLY', points: [] }),
    });
    routeStops.getRouteStops.mockResolvedValueOnce([]);
    state.findLatest.mockResolvedValueOnce(null);

    const response = await getContext(VALID_TOKEN);

    expect(response.status).toBe(200);
    expect(response.body.data).toMatchObject({
      lastUpdatedAt: null,
      vehicle: { location: null },
      route: { originName: 'Origin', destinationName: 'Destination', geometry: null },
      eta: null,
    });
  });

  it('returns a 503 envelope when the detailed route provider is unavailable', async () => {
    routes.getDetailedRouteGeometry.mockResolvedValueOnce({ kind: 'unavailable' });
    const response = await getContext(VALID_TOKEN);
    expect(response.status).toBe(503);
    expect(response.body.error?.code).toBe('TRACKING_TRIP_UNAVAILABLE');
    assertNoStoreHeaders(response.headers);
  });

  it('publishes the public header and ADR0004 envelope in Swagger', () => {
    const document = SwaggerModule.createDocument(
      app,
      new DocumentBuilder().setTitle('Tracking test').setVersion('1').build(),
    );
    const operation = document.paths['/v1/tracking/shared-trip/context']?.get;
    expect(operation?.parameters).toEqual(expect.arrayContaining([
      expect.objectContaining({ name: 'X-Trip-Share-Token', in: 'header', required: true }),
    ]));
    expect(operation?.responses?.['200']).toBeDefined();
    expect(document.components?.schemas?.['TripShareContextEnvelopeSwaggerDto']).toHaveProperty(
      'properties.meta',
    );
  });

  async function getContext(rawToken?: string): Promise<{
    status: number;
    headers: Headers;
    body: Envelope<Record<string, unknown>>;
  }> {
    const response = await fetch(`http://127.0.0.1:${port}/v1/tracking/shared-trip/context`, {
      headers: rawToken ? { 'X-Trip-Share-Token': rawToken } : {},
    });
    return {
      status: response.status,
      headers: response.headers,
      body: await response.json() as Envelope<Record<string, unknown>>,
    };
  }

});

function assertNoStoreHeaders(headers: Headers): void {
  expect(headers.get('cache-control')).toBe('no-store');
  expect(headers.get('pragma')).toBe('no-cache');
  expect(headers.get('referrer-policy')).toBe('no-referrer');
}

function createRoute(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    tripId: TRIP_ID,
    geometrySource: 'ROUTE_POLYLINE',
    points: [{ latitude: 10, longitude: 106 }, { latitude: 10.2, longitude: 106.2 }],
    originStation: { stationId: ORIGIN_ID, name: 'Origin', latitude: 10, longitude: 106 },
    intermediateStops: [],
    destinationStation: {
      stationId: DESTINATION_ID,
      name: 'Destination',
      latitude: 10.2,
      longitude: 106.2,
    },
    ...overrides,
  };
}
