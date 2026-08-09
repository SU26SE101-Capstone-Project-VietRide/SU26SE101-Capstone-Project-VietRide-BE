import { Inject, Injectable } from '@nestjs/common';
import { z } from 'zod';
import { randomUUID } from 'node:crypto';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import {
  TrackingInternalJwtClaims,
  TrackingInternalJwtSigner,
} from '../authorization/tracking-internal-jwt.signer';
import type {
  DetailedRouteGeometryProvider,
  RouteGeometryFetchResult,
  RouteGeometryPoint,
  RouteGeometrySnapshot,
} from './route-geometry.provider';
import { ETA_STOPS_ONLY_CACHE_TTL_SECONDS } from '../eta/eta.constants';

const routeGeometryPointSchema = z.object({
  latitude: z.number(),
  longitude: z.number(),
});

const routeGeometryStationSchema = z.object({
  stationId: z.string().uuid(),
  name: z.string(),
  latitude: z.number(),
  longitude: z.number(),
});

const routeGeometryIntermediateStopSchema = z.object({
  stopId: z.string().uuid(),
  name: z.string(),
  sequence: z.number().int(),
  latitude: z.number(),
  longitude: z.number(),
});

const routeGeometryDataSchema = z.object({
  tripId: z.string().uuid(),
  effectiveRouteId: z.string().uuid().optional(),
  points: z.array(routeGeometryPointSchema),
  alertRecipientUserIds: z.array(z.string()).nullish(),
  geometrySource: z.enum(['ROUTE_POLYLINE', 'STOPS_ONLY']).optional(),
  originStation: routeGeometryStationSchema.nullish(),
  intermediateStops: z.array(routeGeometryIntermediateStopSchema).optional(),
  destinationStation: routeGeometryStationSchema.nullish(),
}).superRefine((value, context) => {
  if (value.geometrySource === 'ROUTE_POLYLINE' && value.points.length < 2) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'ROUTE_POLYLINE requires at least two points',
      path: ['points'],
    });
  }
});

const apiResponseEnvelopeSchema = z.object({
  success: z.literal(true),
  data: z.unknown(),
});

const INTERNAL_AUTH_HEADER = 'X-Internal-Auth';
const BEARER_PREFIX = 'Bearer ';

interface CacheEntry {
  result: RouteGeometryFetchResult;
  expiresAt: number;
}

@Injectable()
export class HttpRouteGeometryProvider implements DetailedRouteGeometryProvider {
  private readonly cache = new Map<string, CacheEntry>();
  private readonly inFlight = new Map<string, Promise<RouteGeometryFetchResult>>();

  constructor(
    @Inject(ENV_TOKEN) private readonly env: Env,
    private readonly signer: TrackingInternalJwtSigner,
  ) {}

  async getRouteGeometry(tripId: string): Promise<RouteGeometrySnapshot | null> {
    const result = await this.getDetailedRouteGeometry(tripId);
    return this.toLegacySnapshot(result);
  }

  async getDetailedRouteGeometry(
    tripId: string,
    options?: { bypassCache?: boolean },
  ): Promise<RouteGeometryFetchResult> {
    const cached = options?.bypassCache ? undefined : this.cache.get(tripId);
    if (cached && Date.now() < cached.expiresAt) {
      return cached.result;
    }

    const existing = options?.bypassCache ? undefined : this.inFlight.get(tripId);
    if (existing) return existing;

    const request = this.fetchRouteGeometry(tripId);
    this.inFlight.set(tripId, request);
    try {
      return await request;
    } finally {
      this.inFlight.delete(tripId);
    }
  }

  peekCachedRouteGeometry(tripId: string): RouteGeometrySnapshot | null {
    const cached = this.cache.get(tripId);
    if (!cached || Date.now() >= cached.expiresAt) return null;
    return this.toLegacySnapshot(cached.result);
  }

  private async fetchRouteGeometry(tripId: string): Promise<RouteGeometryFetchResult> {
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

      if (response.status === 404) {
        return this.cacheTransient(tripId, { kind: 'not_found' });
      }
      if (!response.ok) {
        return this.cacheTransient(tripId, { kind: 'unavailable' });
      }

      const body = await response.json();

      const envelope = apiResponseEnvelopeSchema.safeParse(body);
      if (!envelope.success) {
        return this.cacheTransient(tripId, { kind: 'unavailable' });
      }

      const parsed = routeGeometryDataSchema.safeParse(envelope.data.data);
      if (!parsed.success || parsed.data.tripId !== tripId) {
        return this.cacheTransient(tripId, { kind: 'unavailable' });
      }

      const points: RouteGeometryPoint[] = parsed.data.points.map((p) => ({
        latitude: p.latitude,
        longitude: p.longitude,
      }));

      const result: RouteGeometrySnapshot = {
        tripId: parsed.data.tripId,
        ...(parsed.data.effectiveRouteId
          ? { effectiveRouteId: parsed.data.effectiveRouteId }
          : {}),
        points,
        ...(parsed.data.geometrySource ? { geometrySource: parsed.data.geometrySource } : {}),
        ...(parsed.data.originStation !== undefined
          ? { originStation: parsed.data.originStation ?? null }
          : {}),
        ...(parsed.data.intermediateStops
          ? { intermediateStops: parsed.data.intermediateStops.map((stop) => ({ ...stop })) }
          : {}),
        ...(parsed.data.destinationStation !== undefined
          ? { destinationStation: parsed.data.destinationStation ?? null }
          : {}),
        ...(parsed.data.alertRecipientUserIds?.length
          ? { alertRecipientUserIds: parsed.data.alertRecipientUserIds }
          : {}),
      };

      const fetchResult: RouteGeometryFetchResult = { kind: 'ok', snapshot: result };

      this.cache.set(tripId, {
        result: fetchResult,
        expiresAt: Date.now() + (
          result.geometrySource === 'STOPS_ONLY'
            ? ETA_STOPS_ONLY_CACHE_TTL_SECONDS
            : this.env.TRACKING_ROUTE_GEOMETRY_CACHE_TTL_SECONDS
        ) * 1000,
      });

      return fetchResult;
    } catch {
      return this.cacheTransient(tripId, { kind: 'unavailable' });
    } finally {
      clearTimeout(timeout);
    }
  }

  private buildUrl(tripId: string): string {
    const path = this.env.TRIP_ROUTE_GEOMETRY_PATH.replace(':tripId', encodeURIComponent(tripId));
    return new URL(path, this.env.TRIP_SERVICE_BASE_URL).toString();
  }

  private cacheTransient(
    tripId: string,
    result: Extract<RouteGeometryFetchResult, { kind: 'not_found' | 'unavailable' }>,
  ): RouteGeometryFetchResult {
    this.cache.set(tripId, { result, expiresAt: Date.now() + 30_000 });
    return result;
  }

  private toLegacySnapshot(result: RouteGeometryFetchResult): RouteGeometrySnapshot | null {
    if (result.kind !== 'ok') return null;
    if (result.snapshot.points.length >= 2) return result.snapshot;

    const intermediatePoints = result.snapshot.intermediateStops
      ? result.snapshot.intermediateStops.map((stop) => ({
          latitude: stop.latitude,
          longitude: stop.longitude,
        }))
      : result.snapshot.points;
    const fallbackPoints: RouteGeometryPoint[] = [
      ...(result.snapshot.originStation
        ? [{
            latitude: result.snapshot.originStation.latitude,
            longitude: result.snapshot.originStation.longitude,
          }]
        : []),
      ...intermediatePoints,
      ...(result.snapshot.destinationStation
        ? [{
            latitude: result.snapshot.destinationStation.latitude,
            longitude: result.snapshot.destinationStation.longitude,
          }]
        : []),
    ];
    if (fallbackPoints.length < 2) return null;
    return { ...result.snapshot, points: fallbackPoints };
  }
}
