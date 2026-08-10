import { Test } from '@nestjs/testing';
import { createHash } from 'node:crypto';
import { RedisService } from '@vietride/nest-redis';
import { ROUTE_GEOMETRY_PROVIDER } from '../off-route/off-route.constants';
import type { RouteGeometryProvider } from '../off-route/route-geometry.provider';
import { LocationService } from './location.service';

const TRIP_ID = '11111111-1111-4111-8111-111111111111';

describe('LocationService Phase 10 raw/published GPS', () => {
  let service: LocationService;
  let redisEval: jest.Mock;
  let routeProvider: RouteGeometryProvider;
  let routePeek: jest.MockedFunction<RouteGeometryProvider['peekCachedRouteGeometry']>;
  let routeGet: jest.MockedFunction<RouteGeometryProvider['getRouteGeometry']>;

  beforeEach(async () => {
    redisEval = jest.fn(async () => 1);
    routePeek = jest.fn((tripId: string) => ({
        tripId,
        points: [{ latitude: 10, longitude: 106 }, { latitude: 10.1, longitude: 106 }],
      }));
    routeGet = jest.fn(async (tripId: string) => { void tripId; return null; });
    routeProvider = {
      peekCachedRouteGeometry: routePeek,
      getRouteGeometry: routeGet,
      invalidateRouteGeometry: jest.fn(),
    };
    const moduleRef = await Test.createTestingModule({
      providers: [
        LocationService,
        { provide: RedisService, useValue: { getClient: () => ({ eval: redisEval }) } },
        { provide: ROUTE_GEOMETRY_PROVIDER, useValue: routeProvider },
      ],
    }).compile();
    service = moduleRef.get(LocationService);
  });

  it('publishes a snapped point while buffering the raw point', async () => {
    const result = await service.recordLocation(createGps(10.05, 106.0001));
    expect(result.event.longitude).toBeCloseTo(106, 6);
    expect(result.rawEvent.longitude).toBe(106.0001);
    const published = JSON.parse(redisEval.mock.calls[0]?.[7] as string);
    const raw = JSON.parse(redisEval.mock.calls[0]?.[8] as string);
    expect(published.longitude).toBeCloseTo(106, 6);
    expect(raw.longitude).toBe(106.0001);
    expect(redisEval.mock.calls[0]?.[6]).toBe(createHash('sha256').update(JSON.stringify(raw)).digest('hex'));
  });

  it('keeps raw coordinates outside the 50 metre snap threshold', async () => {
    const result = await service.recordLocation(createGps(10.05, 106.001));
    expect(result.event).toEqual(result.rawEvent);
  });

  it('does not await geometry warming on a cache miss', async () => {
    routePeek.mockReturnValue(null);
    routeGet.mockImplementation(() => new Promise(() => undefined));
    await expect(service.recordLocation(createGps(10.05, 106.0001))).resolves.toEqual(expect.objectContaining({ duplicate: false }));
    expect(routeGet).toHaveBeenCalledWith(TRIP_ID);
  });

  function createGps(latitude: number, longitude: number) {
    return { tripId: TRIP_ID, latitude, longitude, recordedAt: '2026-07-31T00:00:00.000Z' };
  }
});
