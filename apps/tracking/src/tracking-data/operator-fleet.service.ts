import { Injectable, ServiceUnavailableException } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import { UpdateLocationSchema } from '../location/dto/update-location.dto';
import { trackingLatestKey } from '../location/location.constants';
import { shuttleLatestKey } from '../shuttle/shuttle.constants';
import { ShuttleGpsUpdateSchema } from '../shuttle/shuttle.dto';
import {
  OperatorShuttleProjectionProvider,
  type OperatorShuttleProjection,
} from './operator-shuttle-projection.provider';
import {
  OperatorTripProjectionProvider,
  type OperatorTripProjection,
} from './operator-trip-projection.provider';

export interface TripFleetLatestItem {
  kind: 'TRIP';
  tripId: string;
  latitude: number;
  longitude: number;
  speedKmh?: number;
  headingDeg?: number;
  recordedAt: string;
  status: string;
}

export interface ShuttleFleetLatestItem {
  kind: 'SHUTTLE';
  shuttleTripId: string;
  mainTripId: string;
  latitude: number;
  longitude: number;
  speedKmh?: number;
  headingDeg?: number;
  recordedAt: string;
  status: 'IN_PROGRESS';
}

export type FleetLatestItem = TripFleetLatestItem | ShuttleFleetLatestItem;

@Injectable()
export class OperatorFleetService {
  constructor(
    private readonly redis: RedisService,
    private readonly trips: OperatorTripProjectionProvider,
    private readonly shuttles: OperatorShuttleProjectionProvider,
  ) {}

  async getLatest(
    operatorId: string,
    status?: string,
    includeShuttle = false,
  ): Promise<{ items: FleetLatestItem[]; generatedAt: string }> {
    const includeActiveShuttles = includeShuttle
      && (status === undefined || status === 'IN_PROGRESS');
    let projections: OperatorTripProjection[];
    let shuttleProjections: OperatorShuttleProjection[];
    try {
      [projections, shuttleProjections] = await Promise.all([
        this.trips.list(operatorId, status),
        includeActiveShuttles ? this.shuttles.list(operatorId) : Promise.resolve([]),
      ]);
    } catch {
      throw this.fleetUnavailable();
    }
    if (projections.length === 0 && shuttleProjections.length === 0) {
      return { items: [], generatedAt: new Date().toISOString() };
    }

    let payloads: Array<string | null>;
    try {
      payloads = await this.redis.getClient().mget(
        ...projections.map((trip) => trackingLatestKey(trip.tripId)),
        ...shuttleProjections.map((shuttle) => shuttleLatestKey(shuttle.shuttleTripId)),
      );
    } catch {
      throw this.fleetUnavailable();
    }
    const items: FleetLatestItem[] = [];
    projections.forEach((projection, index) => {
      const payload = payloads[index];
      if (!payload) return;
      try {
        const parsed = UpdateLocationSchema.safeParse(JSON.parse(payload));
        if (!parsed.success || parsed.data.tripId !== projection.tripId) return;
        items.push({
          kind: 'TRIP',
          tripId: parsed.data.tripId,
          latitude: parsed.data.latitude,
          longitude: parsed.data.longitude,
          ...(parsed.data.speedKmh !== undefined ? { speedKmh: parsed.data.speedKmh } : {}),
          ...(parsed.data.headingDeg !== undefined ? { headingDeg: parsed.data.headingDeg } : {}),
          recordedAt: parsed.data.recordedAt,
          status: projection.status,
        });
      } catch { /* Ignore a malformed or expired partial item. */ }
    });

    shuttleProjections.forEach((projection, index) => {
      const payload = payloads[projections.length + index];
      if (!payload) return;
      try {
        const parsed = ShuttleGpsUpdateSchema.safeParse(JSON.parse(payload));
        if (!parsed.success || parsed.data.shuttleTripId !== projection.shuttleTripId) return;
        items.push({
          kind: 'SHUTTLE',
          shuttleTripId: parsed.data.shuttleTripId,
          mainTripId: projection.mainTripId,
          latitude: parsed.data.latitude,
          longitude: parsed.data.longitude,
          ...(parsed.data.speedKmh !== undefined ? { speedKmh: parsed.data.speedKmh } : {}),
          ...(parsed.data.heading !== undefined ? { headingDeg: parsed.data.heading } : {}),
          recordedAt: parsed.data.recordedAt,
          status: projection.status,
        });
      } catch { /* Ignore a malformed or expired partial item. */ }
    });
    return { items, generatedAt: new Date().toISOString() };
  }

  private fleetUnavailable(): ServiceUnavailableException {
    return new ServiceUnavailableException({
      errorCode: 'TRACKING_FLEET_UNAVAILABLE',
      detail: 'Operator fleet tracking data is unavailable',
    });
  }
}
