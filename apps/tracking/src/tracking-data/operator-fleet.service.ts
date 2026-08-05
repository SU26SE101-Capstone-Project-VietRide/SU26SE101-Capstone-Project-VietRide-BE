import { Injectable } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import { UpdateLocationSchema } from '../location/dto/update-location.dto';
import { trackingLatestKey } from '../location/location.constants';
import { OperatorTripProjectionProvider } from './operator-trip-projection.provider';

export interface FleetLatestItem {
  tripId: string;
  latitude: number;
  longitude: number;
  speedKmh?: number;
  headingDeg?: number;
  recordedAt: string;
  status: string;
}

@Injectable()
export class OperatorFleetService {
  constructor(
    private readonly redis: RedisService,
    private readonly trips: OperatorTripProjectionProvider,
  ) {}

  async getLatest(operatorId: string, status?: string): Promise<{ items: FleetLatestItem[]; generatedAt: string }> {
    const projections = await this.trips.list(operatorId, status);
    if (projections.length === 0) return { items: [], generatedAt: new Date().toISOString() };
    const payloads = await this.redis.getClient().mget(
      ...projections.map((trip) => trackingLatestKey(trip.tripId)),
    );
    const items: FleetLatestItem[] = [];
    payloads.forEach((payload, index) => {
      if (!payload) return;
      try {
        const parsed = UpdateLocationSchema.safeParse(JSON.parse(payload));
        const projection = projections[index];
        if (!parsed.success || !projection || parsed.data.tripId !== projection.tripId) return;
        items.push({
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
    return { items, generatedAt: new Date().toISOString() };
  }
}
