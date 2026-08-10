import { Inject, Injectable } from '@nestjs/common';
import { z } from 'zod';
import { randomUUID } from 'node:crypto';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import {
  TrackingInternalJwtClaims,
  TrackingInternalJwtSigner,
} from '../authorization/tracking-internal-jwt.signer';
import type { TripDataProvider, TripStopSnapshot } from './trip-data.provider';

const RouteStopSchema = z.object({
  stopId: z.string(),
  stopName: z.string().nullish(),
  latitude: z.number(),
  longitude: z.number(),
  sequence: z.number().int().min(0),
  status: z.string().nullish(),
  alertRecipientUserIds: z.array(z.string()).nullish(),
  estimatedArrivalTime: z.string().nullish(),
});

const RouteStopsDataSchema = z.object({
  stops: z.array(RouteStopSchema),
});

const ApiResponseEnvelopeSchema = z.object({
  success: z.literal(true),
  data: z.unknown(),
});

const INTERNAL_AUTH_HEADER = 'X-Internal-Auth';
const BEARER_PREFIX = 'Bearer ';

interface CacheEntry {
  data: TripStopSnapshot[];
  expiresAt: number;
}

@Injectable()
export class HttpTripDataProvider implements TripDataProvider {
  private readonly cache = new Map<string, CacheEntry>();
  private readonly cacheVersions = new Map<string, number>();

  constructor(
    @Inject(ENV_TOKEN) private readonly env: Env,
    private readonly signer: TrackingInternalJwtSigner,
  ) {}

  async getRouteStops(tripId: string): Promise<TripStopSnapshot[]> {
    const cached = this.cache.get(tripId);
    if (cached && Date.now() < cached.expiresAt) {
      return cached.data;
    }

    const cacheVersion = this.cacheVersions.get(tripId) ?? 0;
    const url = this.buildUrl(tripId);
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), this.env.TRACKING_DATA_PROVIDER_TIMEOUT_MS);

    try {
      const claims: TrackingInternalJwtClaims = {
        sub: 'tracking-service',
        reqId: randomUUID(),
      };
      const token = await this.signer.sign(claims);
      const response = await fetch(url, {
        method: 'GET',
        headers: {
          [INTERNAL_AUTH_HEADER]: `${BEARER_PREFIX}${token}`,
        },
        signal: controller.signal,
      });

      if (!response.ok) {
        return [];
      }

      const body = await response.json();

      const envelope = ApiResponseEnvelopeSchema.safeParse(body);
      if (!envelope.success) return [];

      const parsed = RouteStopsDataSchema.safeParse(envelope.data.data);
      if (!parsed.success) return [];

      const stops: TripStopSnapshot[] = parsed.data.stops.map((stop) => ({
        stopId: stop.stopId,
        ...(stop.stopName != null ? { stopName: stop.stopName } : {}),
        latitude: stop.latitude,
        longitude: stop.longitude,
        sequence: stop.sequence,
        ...(stop.status != null ? { status: stop.status } : {}),
        ...(stop.alertRecipientUserIds?.length ? { alertRecipientUserIds: stop.alertRecipientUserIds } : {}),
        ...(stop.estimatedArrivalTime != null ? { estimatedArrivalTime: stop.estimatedArrivalTime } : {}),
      }));

      if ((this.cacheVersions.get(tripId) ?? 0) === cacheVersion) {
        this.cache.set(tripId, {
          data: stops,
          expiresAt: Date.now() + this.env.TRACKING_ROUTE_STOPS_CACHE_TTL_SECONDS * 1000,
        });
      }

      return stops;
    } catch {
      return [];
    } finally {
      clearTimeout(timeout);
    }
  }

  invalidateRouteStops(tripId: string): void {
    this.cacheVersions.set(tripId, (this.cacheVersions.get(tripId) ?? 0) + 1);
    this.cache.delete(tripId);
  }

  private buildUrl(tripId: string): string {
    const path = this.env.TRIP_ROUTE_STOPS_PATH.replace(':tripId', encodeURIComponent(tripId));
    return new URL(path, this.env.TRIP_SERVICE_BASE_URL).toString();
  }
}
