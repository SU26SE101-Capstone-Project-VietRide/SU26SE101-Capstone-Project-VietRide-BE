import { Injectable } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import { trackingEtaKey, trackingLatestKey } from '../location/location.constants';
import { trackingTripDelayStateKey } from '../trip-delay/trip-delay.constants';
import { UpdateLocationSchema } from '../location/dto/update-location.dto';
import { TrackingPrismaService } from '../prisma/tracking-prisma.service';
import { EtaBaseResponseSchema, type EtaResponseDto } from './dto/eta-response.dto';
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

  async findTrail(
    tripId: string,
    query: TrailQueryDto,
  ): Promise<{ items: TrackingTrailPointDto[]; totalItems: number }> {
    const where = {
      tripId,
      recordedAt: {
        ...(query.from ? { gte: new Date(query.from) } : {}),
        ...(query.to ? { lte: new Date(query.to) } : {}),
      },
    };

    const orderBy = { [query.sortBy]: query.sortDir } as const;

    const [rows, totalItems] = await Promise.all([
      this.prisma.gpsTrail.findMany({
        where,
        orderBy,
        skip: (query.page - 1) * query.pageSize,
        take: query.pageSize,
      }),
      this.prisma.gpsTrail.count({ where }),
    ]);

    return {
      items: rows.map((row) => ({
        id: row.id,
        tripId: row.tripId,
        latitude: Number(row.latitude),
        longitude: Number(row.longitude),
        ...(row.speedKmh !== null ? { speedKmh: Number(row.speedKmh) } : {}),
        ...(row.headingDeg !== null ? { headingDeg: Number(row.headingDeg) } : {}),
        recordedAt: row.recordedAt.toISOString(),
      })),
      totalItems,
    };
  }

  async findEta(tripId: string, stopId: string): Promise<EtaResponseDto | null> {
    const payload = await this.redis.getClient().get(trackingEtaKey(tripId, stopId));
    if (!payload) return null;

    let parsedJson: unknown;
    try {
      parsedJson = JSON.parse(payload);
    } catch {
      return null;
    }

    const parsed = EtaBaseResponseSchema.safeParse(parsedJson);
    if (!parsed.success || parsed.data.tripId !== tripId || parsed.data.stopId !== stopId) return null;
    const baseEta: Omit<EtaResponseDto, 'delayed' | 'delayStatus' | 'delayMinutes'> = parsed.data;

    type DelayState = {
      stopId: string;
      delayStatus: 'DELAYED' | 'ON_TIME';
      delayMinutes: number;
    };
    let state: DelayState | null = null;
    const stateKeys = [
      trackingTripDelayStateKey(tripId, baseEta.stopId),
      trackingTripDelayStateKey(tripId),
    ];
    for (const stateKey of stateKeys) {
      const statePayload = await this.redis.getClient().get(stateKey);
      if (!statePayload) continue;

      try {
        const candidate = JSON.parse(statePayload) as Partial<DelayState> & { tripId?: unknown };
        if (
          candidate.tripId === tripId &&
          candidate.stopId === baseEta.stopId &&
          (candidate.delayStatus === 'DELAYED' || candidate.delayStatus === 'ON_TIME') &&
          typeof candidate.delayMinutes === 'number' &&
          Number.isFinite(candidate.delayMinutes) &&
          candidate.delayMinutes >= 0
        ) {
          state = {
            stopId: candidate.stopId,
            delayStatus: candidate.delayStatus,
            delayMinutes: candidate.delayMinutes,
          };
          break;
        }
      } catch {
        // Try the legacy trip-level key before returning UNKNOWN.
      }
    }

    const stateForEta = state !== null && state.stopId === baseEta.stopId ? state : null;

    return {
      ...baseEta,
      delayed: stateForEta?.delayStatus === 'DELAYED' ? true : stateForEta ? false : null,
      delayStatus: stateForEta?.delayStatus ?? 'UNKNOWN',
      delayMinutes: stateForEta?.delayMinutes ?? null,
    };
  }
}
