import { Injectable } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import { trackingEtaKey, trackingLatestKey } from '../location/location.constants';
import { UpdateLocationSchema } from '../location/dto/update-location.dto';
import { TrackingPrismaService } from '../prisma/tracking-prisma.service';
import type { TrailQueryDto } from './dto/tracking-data-query.dto';

export interface TrackingLatestDto {
  tripId: string;
  latitude: number;
  longitude: number;
  speedKmh?: number;
  headingDeg?: number;
  recordedAt: string;
}

export interface TrackingTrailPointDto extends TrackingLatestDto {
  id: string;
}

@Injectable()
export class TrackingDataRepository {
  constructor(
    private readonly redis: RedisService,
    private readonly prisma: TrackingPrismaService,
  ) {}

  async findLatest(tripId: string): Promise<TrackingLatestDto | null> {
    const payload = await this.redis.getClient().get(trackingLatestKey(tripId));
    if (!payload) return null;

    let parsedJson: unknown;
    try {
      parsedJson = JSON.parse(payload);
    } catch {
      return null;
    }

    const parsed = UpdateLocationSchema.safeParse(parsedJson);
    if (!parsed.success) return null;

    return {
      tripId: parsed.data.tripId,
      latitude: parsed.data.latitude,
      longitude: parsed.data.longitude,
      ...(parsed.data.speedKmh !== undefined ? { speedKmh: parsed.data.speedKmh } : {}),
      ...(parsed.data.headingDeg !== undefined ? { headingDeg: parsed.data.headingDeg } : {}),
      recordedAt: parsed.data.recordedAt,
    };
  }

  async findTrail(tripId: string, query: TrailQueryDto): Promise<TrackingTrailPointDto[]> {
    const rows = await this.prisma.gpsTrail.findMany({
      where: {
        tripId,
        recordedAt: {
          ...(query.from ? { gte: new Date(query.from) } : {}),
          ...(query.to ? { lte: new Date(query.to) } : {}),
        },
      },
      orderBy: { recordedAt: 'asc' },
      take: query.limit,
    });

    return rows.map((row) => ({
      id: row.id,
      tripId: row.tripId,
      latitude: Number(row.latitude),
      longitude: Number(row.longitude),
      ...(row.speedKmh !== null ? { speedKmh: Number(row.speedKmh) } : {}),
      ...(row.headingDeg !== null ? { headingDeg: Number(row.headingDeg) } : {}),
      recordedAt: row.recordedAt.toISOString(),
    }));
  }

  async findEta(tripId: string, stopId: string): Promise<unknown | null> {
    const payload = await this.redis.getClient().get(trackingEtaKey(tripId, stopId));
    if (!payload) return null;

    try {
      return JSON.parse(payload) as unknown;
    } catch {
      return null;
    }
  }
}
