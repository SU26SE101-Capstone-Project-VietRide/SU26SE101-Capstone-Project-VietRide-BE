import { Inject, Injectable } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import { createHash } from 'crypto';
import {
  TRACKING_ACTIVE_TRIPS_KEY,
  TRACKING_GPS_IDEMPOTENCY_TTL_SECONDS,
  TRACKING_LATEST_TTL_SECONDS,
  ROUTE_SNAP_THRESHOLD_METERS,
  trackingGpsBufferKey,
  trackingGpsIdempotencyKey,
  trackingLatestKey,
} from './location.constants';
import type { UpdateLocationDto } from './dto/update-location.dto';
import { ROUTE_GEOMETRY_PROVIDER } from '../off-route/off-route.constants';
import { projectPointToRoute, type RouteGeometryProvider } from '../off-route/route-geometry.provider';

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
  rawEvent: GpsUpdateEvent;
  duplicate: boolean;
}

const RECORD_GPS_SCRIPT = `
local existing = redis.call('GET', KEYS[1])
if existing then
  if existing == ARGV[1] then return 0 end
  return -1
end
redis.call('SET', KEYS[1], ARGV[1], 'EX', tonumber(ARGV[4]))
redis.call('SET', KEYS[2], ARGV[2], 'EX', tonumber(ARGV[5]))
redis.call('RPUSH', KEYS[3], ARGV[3])
redis.call('SADD', KEYS[4], ARGV[6])
return 1
`;

const GPS_ACCEPTED = 1;
const GPS_DUPLICATE = 0;
const GPS_PAYLOAD_MISMATCH = -1;

@Injectable()
export class LocationService {
  constructor(
    private readonly redis: RedisService,
    @Inject(ROUTE_GEOMETRY_PROVIDER) private readonly routeGeometryProvider: RouteGeometryProvider,
  ) {}

  async recordLocation(dto: UpdateLocationDto): Promise<LocationRecordResult> {
    const rawEvent: GpsUpdateEvent = {
      tripId: dto.tripId,
      latitude: dto.latitude,
      longitude: dto.longitude,
      recordedAt: dto.recordedAt,
    };
    if (dto.speedKmh !== undefined) rawEvent.speedKmh = dto.speedKmh;
    if (dto.headingDeg !== undefined) rawEvent.headingDeg = dto.headingDeg;

    const route = this.routeGeometryProvider.peekCachedRouteGeometry(dto.tripId);
    const projection = route ? projectPointToRoute(rawEvent, route.points) : null;
    if (!route) {
      void this.routeGeometryProvider.getRouteGeometry(dto.tripId).catch(() => null);
    }
    const event: GpsUpdateEvent = projection && projection.distanceMeters <= ROUTE_SNAP_THRESHOLD_METERS
      ? { ...rawEvent, latitude: projection.point.latitude, longitude: projection.point.longitude }
      : rawEvent;

    const client = this.redis.getClient();
    const rawPayload = JSON.stringify(rawEvent);
    const publishedPayload = JSON.stringify(event);
    const fingerprint = createHash('sha256').update(rawPayload).digest('hex');
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
          publishedPayload,
          rawPayload,
          String(TRACKING_GPS_IDEMPOTENCY_TTL_SECONDS),
          String(TRACKING_LATEST_TTL_SECONDS),
          dto.tripId,
        ),
      );

      if (result === GPS_DUPLICATE) {
        return { event, rawEvent, duplicate: true };
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

    return { event, rawEvent, duplicate: false };
  }
}
