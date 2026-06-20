import { Inject, Injectable } from '@nestjs/common';
import { z } from 'zod';
import { randomUUID } from 'node:crypto';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import {
  TrackingInternalJwtClaims,
  TrackingInternalJwtSigner,
} from '../authorization/tracking-internal-jwt.signer';
import type { RouteGeometryPoint, RouteGeometryProvider, RouteGeometrySnapshot } from './route-geometry.provider';

const RouteGeometryPointSchema = z.object({
  latitude: z.number(),
  longitude: z.number(),
});

const RouteGeometryDataSchema = z.object({
  tripId: z.string(),
  points: z.array(RouteGeometryPointSchema),
  alertRecipientUserIds: z.array(z.string()).optional(),
});

const ApiResponseEnvelopeSchema = z.object({
  success: z.literal(true),
  data: z.unknown(),
});

const INTERNAL_AUTH_HEADER = 'X-Internal-Auth';
const BEARER_PREFIX = 'Bearer ';

interface CacheEntry {
  data: RouteGeometrySnapshot | null;
  expiresAt: number;
}

@Injectable()
export class HttpRouteGeometryProvider implements RouteGeometryProvider {
  private readonly cache = new Map<string, CacheEntry>();

  constructor(
    @Inject(ENV_TOKEN) private readonly env: Env,
    private readonly signer: TrackingInternalJwtSigner,
  ) {}

  async getRouteGeometry(tripId: string): Promise<RouteGeometrySnapshot | null> {
    const cached = this.cache.get(tripId);
    if (cached && Date.now() < cached.expiresAt) {
      return cached.data;
    }

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
        this.cache.set(tripId, { data: null, expiresAt: Date.now() + 30_000 });
        return null;
      }

      const body = await response.json();

      const envelope = ApiResponseEnvelopeSchema.safeParse(body);
      if (!envelope.success) {
        this.cache.set(tripId, { data: null, expiresAt: Date.now() + 30_000 });
        return null;
      }

      const parsed = RouteGeometryDataSchema.safeParse(envelope.data.data);
      if (!parsed.success || parsed.data.points.length < 2) {
        this.cache.set(tripId, { data: null, expiresAt: Date.now() + 30_000 });
        return null;
      }

      const points: RouteGeometryPoint[] = parsed.data.points.map((p) => ({
        latitude: p.latitude,
        longitude: p.longitude,
      }));

      const result: RouteGeometrySnapshot = {
        tripId: parsed.data.tripId,
        points,
        ...(parsed.data.alertRecipientUserIds?.length
          ? { alertRecipientUserIds: parsed.data.alertRecipientUserIds }
          : {}),
      };

      this.cache.set(tripId, {
        data: result,
        expiresAt: Date.now() + this.env.TRACKING_ROUTE_GEOMETRY_CACHE_TTL_SECONDS * 1000,
      });

      return result;
    } catch {
      this.cache.set(tripId, { data: null, expiresAt: Date.now() + 30_000 });
      return null;
    } finally {
      clearTimeout(timeout);
    }
  }

  private buildUrl(tripId: string): string {
    const path = this.env.TRIP_ROUTE_GEOMETRY_PATH.replace(':tripId', encodeURIComponent(tripId));
    return new URL(path, this.env.TRIP_SERVICE_BASE_URL).toString();
  }
}
