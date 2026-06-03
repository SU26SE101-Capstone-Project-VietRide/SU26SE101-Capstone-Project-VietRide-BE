import { Injectable, Logger } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import type { UpdateLocationDto } from '../location/dto/update-location.dto';
import { TrackingPrismaService } from '../prisma/tracking-prisma.service';
import {
  TRACKING_ACTIVE_TRIPS_KEY,
  trackingGpsBufferKey,
} from '../location/location.constants';
import { UpdateLocationSchema } from '../location/dto/update-location.dto';

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
      const rows = await client.lrange(bufferKey, 0, -1);
      if (rows.length === 0) continue;

      const parsedRows = rows.flatMap((row) => this.parseBufferedRow(row, tripId));

      if (parsedRows.length === 0) {
        this.logger.warn(`No valid GPS rows found in buffer for trip ${tripId}`);
        continue;
      }

      await this.prisma.gpsTrail.createMany({
        data: parsedRows.map((row) => {
          const data = {
            tripId: row.tripId,
            latitude: row.latitude,
            longitude: row.longitude,
            recordedAt: new Date(row.recordedAt),
            ...(row.speedKmh !== undefined ? { speedKmh: row.speedKmh } : {}),
            ...(row.headingDeg !== undefined ? { headingDeg: row.headingDeg } : {}),
          };
          return data;
        }),
      });

      await client.del(bufferKey);
      inserted += parsedRows.length;
    }

    this.logger.log(`Flushed ${inserted} GPS trail rows`);
    return inserted;
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
