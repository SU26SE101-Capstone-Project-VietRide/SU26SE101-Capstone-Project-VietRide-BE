import { randomUUID } from 'node:crypto';
import IORedis from 'ioredis';
import { RedisService } from '@vietride/nest-redis';
import type { TripDataProvider } from '../eta/trip-data.provider';
import { trackingEtaKey } from '../location/location.constants';
import type { DetailedRouteGeometryProvider } from '../off-route/route-geometry.provider';
import type { TrackingPrismaService } from '../prisma/tracking-prisma.service';
import { TrackingDataRepository } from './tracking-data.repository';
import { TrackingDataService } from './tracking-data.service';

const describeSystem = process.env.TRACKING_ETA_SYSTEM_E2E === '1' ? describe : describe.skip;

describeSystem('Tracking destination ETA (real Redis)', () => {
  const tripId = randomUUID();
  const stationId = randomUUID();
  const redisKey = trackingEtaKey(tripId, stationId);
  let client: IORedis;
  let service: TrackingDataService;

  beforeAll(async () => {
    client = new IORedis(process.env.REDIS_URL as string, { maxRetriesPerRequest: null });
    const repository = new TrackingDataRepository(
      new RedisService(client),
      {} as TrackingPrismaService,
    );
    const tripData: TripDataProvider = {
      getRouteStops: async () => [],
      invalidateRouteStops: () => undefined,
    };
    const routeGeometry: DetailedRouteGeometryProvider = {
      peekCachedRouteGeometry: () => null,
      getRouteGeometry: async () => null,
      invalidateRouteGeometry: () => undefined,
      getDetailedRouteGeometry: async () => ({
        kind: 'ok',
        snapshot: {
          tripId,
          points: [],
          destinationStation: {
            stationId,
            name: 'Destination station',
            latitude: 10.77,
            longitude: 106.69,
          },
        },
      }),
    };
    service = new TrackingDataService(repository, tripData, routeGeometry);
  });

  afterAll(async () => {
    await client.del(redisKey);
    await client.quit();
  });

  it('returns a discriminated STATION response only for the effective destination', async () => {
    await client.set(redisKey, JSON.stringify({
      tripId,
      targetKind: 'STATION',
      stationId,
      etaMinutes: 18,
      estimatedArrivalTime: new Date(Date.now() + 18 * 60_000).toISOString(),
      distanceMeters: 12_500,
      updatedAt: new Date().toISOString(),
    }), 'EX', 60);

    await expect(service.getEta(tripId, { targetKind: 'STATION', stationId })).resolves.toEqual({
      eta: expect.objectContaining({
        targetKind: 'STATION',
        stationId,
        stopName: 'Destination station',
        etaMinutes: 18,
      }),
    });
    await expect(
      service.getEta(tripId, { targetKind: 'STATION', stationId: randomUUID() }),
    ).resolves.toEqual({ eta: null });
  });
});
