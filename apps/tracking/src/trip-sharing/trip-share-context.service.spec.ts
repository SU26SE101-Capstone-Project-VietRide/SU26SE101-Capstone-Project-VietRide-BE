import { HttpStatus } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import type { DetailedRouteGeometryProvider, RouteGeometrySnapshot } from '../off-route/route-geometry.provider';
import type { TripShareAccessContext } from './trip-share-access.service';
import { TripShareContextService } from './trip-share-context.service';
import type { TripShareRouteStopsProvider } from './trip-share-route-stops.provider';
import { TripShareTrackingStateRepository } from './trip-share-tracking-state.repository';

const TRIP_ID = '11111111-1111-4111-8111-111111111111';
const GRANT_ID = '22222222-2222-4222-8222-222222222222';
const ORIGIN_ID = '33333333-3333-4333-8333-333333333333';
const DESTINATION_ID = '44444444-4444-4444-8444-444444444444';
const COMPLETED_STOP_ID = '55555555-5555-4555-8555-555555555555';
const NEXT_STOP_ID = '66666666-6666-4666-8666-666666666666';
const PASSENGER_PHONE = '0900000000';
const EXPIRES_AT = new Date('2026-08-04T10:00:00.000Z');
const ACCESS: TripShareAccessContext = {
  grantId: GRANT_ID,
  tripId: TRIP_ID,
  expiresAt: EXPIRES_AT,
  status: 'IN_PROGRESS',
};

describe('TripShareContextService', () => {
  const routeProvider = { getDetailedRouteGeometry: jest.fn() };
  const tripDataProvider = { getRouteStops: jest.fn() };
  const state = { findLatest: jest.fn(), findEta: jest.fn() };
  let service: TripShareContextService;

  beforeEach(() => {
    jest.clearAllMocks();
    service = new TripShareContextService(
      routeProvider as unknown as DetailedRouteGeometryProvider,
      tripDataProvider as unknown as TripShareRouteStopsProvider,
      state as unknown as TripShareTrackingStateRepository,
    );
    routeProvider.getDetailedRouteGeometry.mockResolvedValue({ kind: 'ok', snapshot: createRoute() });
    tripDataProvider.getRouteStops.mockResolvedValue([
      { stopId: NEXT_STOP_ID, sequence: 2, latitude: 10.2, longitude: 106.2, status: 'PENDING' },
      { stopId: COMPLETED_STOP_ID, sequence: 1, latitude: 10.1, longitude: 106.1, status: 'COMPLETED' },
    ]);
    state.findLatest.mockResolvedValue({
      tripId: TRIP_ID,
      latitude: 10.05,
      longitude: 106.05,
      speedKmh: 42,
      headingDeg: 95,
      recordedAt: '2026-08-03T10:01:00.000Z',
    });
    state.findEta.mockResolvedValue({
      tripId: TRIP_ID,
      stopId: NEXT_STOP_ID,
      etaMinutes: 12,
      estimatedArrivalTime: '2026-08-03T10:20:00.000Z',
      distanceMeters: 8_500,
      updatedAt: '2026-08-03T10:02:00.000Z',
    });
  });

  it('maps the exact anonymous allow-list with GeoJSON longitude-latitude order', async () => {
    const result = await service.getContext(ACCESS);

    expect(result).toEqual({
      status: 'IN_PROGRESS',
      expiresAt: EXPIRES_AT.toISOString(),
      lastUpdatedAt: '2026-08-03T10:02:00.000Z',
      vehicle: {
        location: {
          latitude: 10.05,
          longitude: 106.05,
          heading: 95,
          speedKph: 42,
          recordedAt: '2026-08-03T10:01:00.000Z',
        },
      },
      route: {
        originName: 'Origin',
        destinationName: 'Destination',
        geometry: {
          type: 'LineString',
          coordinates: [[106, 10], [106.2, 10.2]],
        },
      },
      eta: {
        estimatedArrivalAt: '2026-08-03T10:20:00.000Z',
        remainingSeconds: 720,
        delayMinutes: null,
        updatedAt: '2026-08-03T10:02:00.000Z',
      },
    });
    expect(state.findEta).toHaveBeenCalledWith(TRIP_ID, NEXT_STOP_ID);
    assertAnonymousPrivacy(result);
  });

  it('returns nullable location, geometry and ETA without synthesizing a route', async () => {
    routeProvider.getDetailedRouteGeometry.mockResolvedValueOnce({
      kind: 'ok',
      snapshot: createRoute({ geometrySource: 'STOPS_ONLY', points: [] }),
    });
    tripDataProvider.getRouteStops.mockResolvedValueOnce([]);
    state.findLatest.mockResolvedValueOnce(null);

    const result = await service.getContext(ACCESS);

    expect(result.vehicle.location).toBeNull();
    expect(result.route).toEqual({ originName: 'Origin', destinationName: 'Destination', geometry: null });
    expect(result.eta).toBeNull();
    expect(result.lastUpdatedAt).toBeNull();
    expect(state.findEta).not.toHaveBeenCalled();
  });

  it('returns null geometry when a polyline has fewer than two valid sanitized points', async () => {
    routeProvider.getDetailedRouteGeometry.mockResolvedValueOnce({
      kind: 'ok',
      snapshot: createRoute({
        points: [
          { latitude: 10, longitude: 106 },
          { latitude: 10, longitude: 106 },
          { latitude: 91, longitude: 106 },
        ],
      }),
    });

    const result = await service.getContext(ACCESS);

    expect(result.route.geometry).toBeNull();
  });

  it('maps absent optional GPS heading and speed to explicit nulls', async () => {
    state.findLatest.mockResolvedValueOnce({
      tripId: TRIP_ID,
      latitude: 10,
      longitude: 106,
      recordedAt: '2026-08-03T10:01:00.000Z',
    });

    const result = await service.getContext(ACCESS);

    expect(result.vehicle.location).toMatchObject({ heading: null, speedKph: null });
  });

  it('filters terminal stops and orders the next ETA stop by sequence', async () => {
    tripDataProvider.getRouteStops.mockResolvedValueOnce([
      { stopId: '77777777-7777-4777-8777-777777777777', sequence: 4, status: 'PENDING' },
      { stopId: NEXT_STOP_ID, sequence: 3, status: 'PENDING' },
      { stopId: COMPLETED_STOP_ID, sequence: 1, status: 'ARRIVED' },
      { stopId: '88888888-8888-4888-8888-888888888888', sequence: 2, status: 'SKIPPED' },
    ]);

    await service.getContext(ACCESS);

    expect(state.findEta).toHaveBeenCalledWith(TRIP_ID, NEXT_STOP_ID);
  });

  it.each([
    { kind: 'not_found' as const },
    { kind: 'unavailable' as const },
    { kind: 'ok' as const, snapshot: { tripId: TRIP_ID, points: [] } },
  ])('fails closed when detailed route context is unavailable or incomplete', async (routeResult) => {
    routeProvider.getDetailedRouteGeometry.mockResolvedValueOnce(routeResult);
    await expect(service.getContext(ACCESS)).rejects.toMatchObject({
      status: HttpStatus.SERVICE_UNAVAILABLE,
      response: { errorCode: 'TRACKING_TRIP_UNAVAILABLE' },
    });
  });

  it('maps route-stops provider transport failures to 503', async () => {
    tripDataProvider.getRouteStops.mockRejectedValueOnce(new Error('transport failed'));
    await expect(service.getContext(ACCESS)).rejects.toMatchObject({
      status: HttpStatus.SERVICE_UNAVAILABLE,
      response: { errorCode: 'TRACKING_TRIP_UNAVAILABLE' },
    });
  });

  it('does not expose fixture UUIDs, passenger PII, route IDs or upstream additive fields', async () => {
    const result = await service.getContext(ACCESS);
    const json = JSON.stringify(result);
    for (const forbidden of [
      TRIP_ID, GRANT_ID, ORIGIN_ID, DESTINATION_ID, COMPLETED_STOP_ID, NEXT_STOP_ID,
      PASSENGER_PHONE, 'alertRecipientUserIds', 'stationId', 'stopId', 'tripId', 'trail',
    ]) {
      expect(json).not.toContain(forbidden);
    }
  });
});

describe('TripShareTrackingStateRepository', () => {
  const redisClient = { get: jest.fn() };
  const redis = { getClient: () => redisClient } as unknown as RedisService;
  const repository = new TripShareTrackingStateRepository(redis);

  beforeEach(() => jest.clearAllMocks());

  it('returns null for missing, malformed, invalid or cross-trip latest Redis payloads', async () => {
    for (const payload of [
      null,
      '{',
      JSON.stringify({ tripId: TRIP_ID, latitude: 91, longitude: 106, recordedAt: 'bad' }),
      JSON.stringify({
        tripId: GRANT_ID,
        latitude: 10,
        longitude: 106,
        recordedAt: '2026-08-03T10:00:00.000Z',
      }),
    ]) {
      redisClient.get.mockResolvedValueOnce(payload);
      await expect(repository.findLatest(TRIP_ID)).resolves.toBeNull();
    }
  });

  it('returns null for malformed or cross-stop ETA Redis payloads', async () => {
    for (const payload of [
      '{',
      JSON.stringify({
        tripId: TRIP_ID,
        stopId: COMPLETED_STOP_ID,
        etaMinutes: 12,
        estimatedArrivalTime: '2026-08-03T10:20:00.000Z',
        distanceMeters: 8_500,
        updatedAt: '2026-08-03T10:02:00.000Z',
      }),
    ]) {
      redisClient.get.mockResolvedValueOnce(payload);
      await expect(repository.findEta(TRIP_ID, NEXT_STOP_ID)).resolves.toBeNull();
    }
  });
});

function createRoute(overrides: Partial<RouteGeometrySnapshot> = {}): RouteGeometrySnapshot {
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
    alertRecipientUserIds: [GRANT_ID],
    passengerPhone: PASSENGER_PHONE,
    ...overrides,
  } as RouteGeometrySnapshot;
}

function assertAnonymousPrivacy(value: unknown): void {
  const forbiddenKeys = new Set([
    'tripId', 'grantId', 'shareId', 'token', 'tokenHash', 'stationId', 'stopId', 'bookingId',
    'ticketId', 'userId', 'operatorId', 'seatNumber', 'email', 'phone', 'passengerUserId',
    'driverUserId', 'assistantUserId', 'alertRecipientUserIds', 'trail',
  ]);
  const visit = (node: unknown): void => {
    if (Array.isArray(node)) return node.forEach(visit);
    if (!node || typeof node !== 'object') return;
    for (const [key, child] of Object.entries(node)) {
      expect(forbiddenKeys.has(key)).toBe(false);
      visit(child);
    }
  };
  visit(value);
}
