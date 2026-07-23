import { Injectable, Logger } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import type { UpdateLocationDto } from '../location/dto/update-location.dto';
import { TrackingPrismaService } from '../prisma/tracking-prisma.service';
import {
  TRACKING_ACTIVE_TRIPS_KEY,
  trackingGpsBufferKey,
  trackingGpsIdleKey,
  trackingGpsProcessingKey,
} from '../location/location.constants';
import { UpdateLocationSchema } from '../location/dto/update-location.dto';
import {
  GPS_BATCH_IDLE_CYCLES_BEFORE_PRUNE,
  GPS_BATCH_IDLE_KEY_TTL_SECONDS,
} from './gps-batch.constants';

/**
 * Atomic Lua: drain buffer → processing slot via RENAMENX.
 * KEYS[1] = buffer key, KEYS[2] = processing key.
 * Returns: { -1 }  → processing slot busy (retry existing first)
 *          { 0 }   → buffer empty
 *          [rows]  → buffer atomically moved to processing, rows follow
 */
const ATOMIC_RENAME_SCRIPT = `
if redis.call('EXISTS', KEYS[2]) == 1 then return {-1} end
if redis.call('EXISTS', KEYS[1]) == 0 then return {0} end
local renamed = redis.call('RENAMENX', KEYS[1], KEYS[2])
if renamed == 0 then return {0} end
return redis.call('LRANGE', KEYS[2], 0, -1)
`;

const MAYBE_PRUNE_IDLE_TRIP_SCRIPT = `
if redis.call('EXISTS', KEYS[3]) == 1 or redis.call('EXISTS', KEYS[4]) == 1 then
  redis.call('DEL', KEYS[2])
  return 0
end
local idle = redis.call('INCR', KEYS[2])
redis.call('EXPIRE', KEYS[2], tonumber(ARGV[3]))
if idle >= tonumber(ARGV[2]) then
  redis.call('SREM', KEYS[1], ARGV[1])
  redis.call('DEL', KEYS[2])
  return 1
end
return 0
`;

const PROCESSING_BUSY = -1;
const BUFFER_EMPTY = 0;

@Injectable()
export class GpsBatchFlushService {
  private readonly logger = new Logger(GpsBatchFlushService.name);

  constructor(
    private readonly redis: RedisService,
    private readonly prisma: TrackingPrismaService,
  ) {}

  async flushOnce(): Promise<number> {
    const client = this.redis.getClient();
    const tripIds = await client.smembers(TRACKING_ACTIVE_TRIPS_KEY);
    let inserted = 0;

    for (const tripId of tripIds) {
      const bufferKey = trackingGpsBufferKey(tripId);
      const idleKey = trackingGpsIdleKey(tripId);
      const processingKey = trackingGpsProcessingKey(tripId);

      const rows = await this.drainTripBuffer(client, bufferKey, processingKey);
      if (rows.length === 0) {
        await this.maybePruneIdleTrip(client, tripId, idleKey, bufferKey, processingKey);
        continue;
      }

      await client.del(idleKey);

      const parsedRows = rows.flatMap((row) => this.parseBufferedRow(row, tripId));
      if (parsedRows.length === 0) {
        await client.del(processingKey);
        continue;
      }

      try {
        const result = await this.prisma.gpsTrail.createMany({
          data: parsedRows.map((row) => ({
            tripId: row.tripId,
            latitude: row.latitude,
            longitude: row.longitude,
            recordedAt: new Date(row.recordedAt),
            ...(row.speedKmh !== undefined ? { speedKmh: row.speedKmh } : {}),
            ...(row.headingDeg !== undefined ? { headingDeg: row.headingDeg } : {}),
          })),
          skipDuplicates: true,
        });

        await client.del(processingKey);
        inserted += result.count;
      } catch (err) {
        // Processing key preserved — next flush cycle will retry it
        this.logger.error(
          `DB insert failed for trip ${tripId}, processing key retained: ${(err as Error).message}`,
        );
        continue;
      }
    }

    this.logger.log(`Flushed ${inserted} GPS trail rows`);
    return inserted;
  }

  private async drainTripBuffer(
    client: ReturnType<RedisService['getClient']>,
    bufferKey: string,
    processingKey: string,
  ): Promise<string[]> {
    const result = (await client.eval(ATOMIC_RENAME_SCRIPT, 2, bufferKey, processingKey)) as
      | number[]
      | string[];

    if (result.length === 1 && result[0] === PROCESSING_BUSY) {
      // Stale processing key exists — retry its data first
      this.logger.warn(`GPS processing key exists for ${bufferKey}, retrying`);
      return (await client.lrange(processingKey, 0, -1)) as string[];
    }

    if (result.length === 1 && result[0] === BUFFER_EMPTY) {
      return [];
    }

    return result as string[];
  }

  private async maybePruneIdleTrip(
    client: ReturnType<RedisService['getClient']>,
    tripId: string,
    idleKey: string,
    bufferKey: string,
    processingKey: string,
  ): Promise<void> {
    const pruned = await client.eval(
      MAYBE_PRUNE_IDLE_TRIP_SCRIPT,
      4,
      TRACKING_ACTIVE_TRIPS_KEY,
      idleKey,
      bufferKey,
      processingKey,
      tripId,
      String(GPS_BATCH_IDLE_CYCLES_BEFORE_PRUNE),
      String(GPS_BATCH_IDLE_KEY_TTL_SECONDS),
    );
    if (pruned === 1) {
      this.logger.log(`Pruned idle tracking trip ${tripId}`);
    }
  }

  private parseBufferedRow(row: string, tripId: string): UpdateLocationDto[] {
    let parsedJson: unknown;
    try {
      parsedJson = JSON.parse(row);
    } catch {
      this.logger.warn(`Skipping malformed GPS JSON row for trip ${tripId}`);
      return [];
    }

    const parsed = UpdateLocationSchema.safeParse(parsedJson);
    if (!parsed.success) {
      this.logger.warn(`Skipping invalid GPS row for trip ${tripId}`);
      return [];
    }

    return [parsed.data];
  }
}
