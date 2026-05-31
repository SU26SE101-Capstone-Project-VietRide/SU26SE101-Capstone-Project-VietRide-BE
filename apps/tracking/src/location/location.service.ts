import { Injectable } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import {
  TRACKING_ACTIVE_TRIPS_KEY,
  TRACKING_LATEST_TTL_SECONDS,
  trackingGpsBufferKey,
  trackingLatestKey,
} from './location.constants';
import type { UpdateLocationDto } from './dto/update-location.dto';

export interface GpsUpdateEvent {
  tripId: string;
  latitude: number;
  longitude: number;
  speedKmh?: number;
  headingDeg?: number;
  recordedAt: string;
}

@Injectable()
export class LocationService {
  constructor(private readonly redis: RedisService) {}

  async recordLocation(dto: UpdateLocationDto): Promise<GpsUpdateEvent> {
    const event: GpsUpdateEvent = {
      tripId: dto.tripId,
      latitude: dto.latitude,
      longitude: dto.longitude,
      recordedAt: dto.recordedAt,
    };
    if (dto.speedKmh !== undefined) event.speedKmh = dto.speedKmh;
    if (dto.headingDeg !== undefined) event.headingDeg = dto.headingDeg;

    const client = this.redis.getClient();
    const payload = JSON.stringify(event);
    await client
      .multi()
      .set(trackingLatestKey(dto.tripId), payload, 'EX', TRACKING_LATEST_TTL_SECONDS)
      .rpush(trackingGpsBufferKey(dto.tripId), payload)
      .sadd(TRACKING_ACTIVE_TRIPS_KEY, dto.tripId)
      .exec();

    return event;
  }
}
