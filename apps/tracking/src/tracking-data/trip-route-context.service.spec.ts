import { HttpStatus } from '@nestjs/common';
import type { DetailedRouteGeometryProvider, RouteGeometrySnapshot } from '../off-route/route-geometry.provider';
import { sanitizeRouteGeometryPoints } from './route-geometry-sanitizer';
import { TripRouteContextService } from './trip-route-context.service';

const TRIP_ID = '11111111-1111-4111-8111-111111111111';

describe('TripRouteContextService', () => {
  it('shares deterministic coordinate sanitization without changing Phase 12 output', () => {
    const points = Array.from({ length: 1_500 }, (_, index) => ({
      latitude: 10 + index / 100_000,
      longitude: 106 + Math.sin(index / 10) / 1_000,
    }));

    const sanitized = sanitizeRouteGeometryPoints([
      { latitude: Number.NaN, longitude: 106 },
      points[0]!,
      points[0]!,
      ...points.slice(1),
      { latitude: 10, longitude: 181 },
    ]);

    expect(sanitized).toHaveLength(1_000);
    expect(sanitized[0]).toEqual(points[0]);
    expect(sanitized.at(-1)).toEqual(points.at(-1));
    expect(sanitizeRouteGeometryPoints(points)).toEqual(sanitized);
  });

  it('sanitizes geometry and markers with an explicit public allow-list', async () => {
    const snapshot = createSnapshot({
      points: [
        { latitude: 10, longitude: 106 },
        { latitude: 10, longitude: 106 },
        { latitude: 91, longitude: 106 },
        { latitude: 10.2, longitude: 106.2 },
      ],
      alertRecipientUserIds: ['99999999-9999-4999-8999-999999999999'],
      originStation: {
        stationId: '22222222-2222-4222-8222-222222222222',
        name: 'Invalid origin',
        latitude: 91,
        longitude: 106,
      },
      intermediateStops: [
        {
          stopId: '44444444-4444-4444-8444-444444444444',
          name: 'Valid stop',
          sequence: 1,
          latitude: 10.1,
          longitude: 106.1,
        },
        {
          stopId: '55555555-5555-4555-8555-555555555555',
          name: 'Invalid stop',
          sequence: 2,
          latitude: -91,
          longitude: 106.2,
        },
      ],
    });
    const service = createService({ kind: 'ok', snapshot });

    const result = await service.getRouteContext(TRIP_ID);

    expect(result.data.geometry?.points).toEqual([
      { latitude: 10, longitude: 106 },
      { latitude: 10.2, longitude: 106.2 },
    ]);
    expect(result.data.intermediateStops).toHaveLength(1);
    expect(result.data.originStation).toBeNull();
    expect(JSON.stringify(result.data)).not.toContain('alertRecipientUserIds');
  });

  it('returns markers but never a synthetic public line for STOPS_ONLY', async () => {
    const snapshot = createSnapshot({ geometrySource: 'STOPS_ONLY' });
    const service = createService({ kind: 'ok', snapshot });

    const result = await service.getRouteContext(TRIP_ID);

    expect(result.data.geometry).toBeNull();
    expect(result.data.originStation).not.toBeNull();
    expect(result.data.destinationStation).not.toBeNull();
  });

  it('preserves the known Phase 12 response hash after sanitizer extraction', async () => {
    const service = createService({ kind: 'ok', snapshot: createSnapshot() });

    const result = await service.getRouteContext(TRIP_ID);

    expect(result.etag).toBe('"aa4b1e0cae370c1f3de57ba5eeb614c22cd487cf24467dcef03dbd13946ec1cf"');
  });

  it('deterministically caps Douglas-Peucker output at 1000 points and keeps endpoints', async () => {
    const points = Array.from({ length: 1_500 }, (_, index) => ({
      latitude: 10 + index / 100_000,
      longitude: 106 + Math.sin(index / 10) / 1_000,
    }));
    const snapshot = createSnapshot({ points });
    const service = createService({ kind: 'ok', snapshot });

    const first = await service.getRouteContext(TRIP_ID);
    const second = await service.getRouteContext(TRIP_ID);

    expect(first.data.geometry?.points).toHaveLength(1_000);
    expect(first.data.geometry?.points[0]).toEqual(points[0]);
    expect(first.data.geometry?.points.at(-1)).toEqual(points.at(-1));
    expect(first.etag).toMatch(/^"[a-f0-9]{64}"$/);
    expect(second.etag).toBe(first.etag);
    expect(second.data).toEqual(first.data);
  });

  it.each([
    [{ kind: 'not_found' } as const, HttpStatus.NOT_FOUND, 'TRIP_NOT_FOUND'],
    [{ kind: 'unavailable' } as const, HttpStatus.SERVICE_UNAVAILABLE, 'TRACKING_ROUTE_CONTEXT_UNAVAILABLE'],
  ])('maps provider result to the public error matrix', async (providerResult, status, errorCode) => {
    const service = createService(providerResult);

    await expect(service.getRouteContext(TRIP_ID)).rejects.toMatchObject({
      status,
      response: { errorCode },
    });
  });

  it('fails closed when additive route context fields are missing', async () => {
    const service = createService({
      kind: 'ok',
      snapshot: { tripId: TRIP_ID, points: [{ latitude: 10, longitude: 106 }] },
    });

    await expect(service.getRouteContext(TRIP_ID)).rejects.toMatchObject({
      status: HttpStatus.SERVICE_UNAVAILABLE,
    });
  });
});

function createService(
  result: Awaited<ReturnType<DetailedRouteGeometryProvider['getDetailedRouteGeometry']>>,
): TripRouteContextService {
  const provider = {
    getDetailedRouteGeometry: jest.fn(async () => result),
  } as unknown as DetailedRouteGeometryProvider;
  return new TripRouteContextService(provider);
}

function createSnapshot(overrides: Partial<RouteGeometrySnapshot> = {}): RouteGeometrySnapshot {
  return {
    tripId: TRIP_ID,
    geometrySource: 'ROUTE_POLYLINE',
    points: [{ latitude: 10, longitude: 106 }, { latitude: 10.2, longitude: 106.2 }],
    originStation: {
      stationId: '22222222-2222-4222-8222-222222222222',
      name: 'Origin',
      latitude: 10,
      longitude: 106,
    },
    intermediateStops: [],
    destinationStation: {
      stationId: '33333333-3333-4333-8333-333333333333',
      name: 'Destination',
      latitude: 10.2,
      longitude: 106.2,
    },
    ...overrides,
  };
}
