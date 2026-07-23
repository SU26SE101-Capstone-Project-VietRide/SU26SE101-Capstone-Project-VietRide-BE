import { Injectable } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import { createHash } from 'crypto';
import {
  TRACKING_ACTIVE_TRIPS_KEY,
  TRACKING_GPS_IDEMPOTENCY_TTL_SECONDS,
  TRACKING_LATEST_TTL_SECONDS,
  trackingGpsBufferKey,
  trackingGpsIdempotencyKey,
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

export interface LocationRecordResult {
  event: GpsUpdateEvent;
  duplicate: boolean;
}

const RECORD_GPS_SCRIPT = `
local existing = redis.call('GET', KEYS[1])
if existing then
  if existing == ARGV[1] then return 0 end
  return -1
end
redis.call('SET', KEYS[1], ARGV[1], 'EX', tonumber(ARGV[3]))
redis.call('SET', KEYS[2], ARGV[2], 'EX', tonumber(ARGV[4]))
redis.call('RPUSH', KEYS[3], ARGV[2])
redis.call('SADD', KEYS[4], ARGV[5])
return 1
`;

const GPS_ACCEPTED = 1;
const GPS_DUPLICATE = 0;
const GPS_PAYLOAD_MISMATCH = -1;

@Injectable()
export class LocationService {
  constructor(private readonly redis: RedisService) {}

  async recordLocation(dto: UpdateLocationDto): Promise<LocationRecordResult> {
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
    const fingerprint = createHash('sha256').update(payload).digest('hex');
    try {
      const result = Number(
        await client.eval(
          RECORD_GPS_SCRIPT,
          4,
          trackingGpsIdempotencyKey(dto.tripId, dto.recordedAt),
          trackingLatestKey(dto.tripId),
          trackingGpsBufferKey(dto.tripId),
          TRACKING_ACTIVE_TRIPS_KEY,
          fingerprint,
          payload,
          String(TRACKING_GPS_IDEMPOTENCY_TTL_SECONDS),
          String(TRACKING_LATEST_TTL_SECONDS),
          dto.tripId,
        ),
      );

      if (result === GPS_DUPLICATE) {
        return { event, duplicate: true };
      }
      if (result === GPS_PAYLOAD_MISMATCH) {
        throw new Error('GPS_OPERATION_PAYLOAD_MISMATCH');
      }
      if (result !== GPS_ACCEPTED) {
        throw new Error(`Unexpected Redis idempotency result: ${result}`);
      }
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      if (message === 'GPS_OPERATION_PAYLOAD_MISMATCH') {
        throw error;
      }
      throw new Error(`TRACKING_REDIS_WRITE_FAILED: ${message}`);
    }

    return { event, duplicate: false };
  }
}
