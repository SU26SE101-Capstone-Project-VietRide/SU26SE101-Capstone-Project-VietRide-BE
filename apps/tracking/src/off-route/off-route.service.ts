import { Inject, Injectable } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import { randomUUID } from 'node:crypto';
import pino from 'pino';
import type { GpsUpdateEvent } from '../location/location.service';
import { TrackingPrismaService } from '../prisma/tracking-prisma.service';
import {
  OFF_ROUTE_CONTINUOUS_THRESHOLD_MS,
  OFF_ROUTE_DISTANCE_THRESHOLD_METERS,
  OFF_ROUTE_EVENT_TYPE,
  OFF_ROUTE_LOCK_TTL_SECONDS,
  OFF_ROUTE_REALTIME_HEARTBEAT_MS,
  OFF_ROUTE_STATE_TTL_SECONDS,
  ROUTE_GEOMETRY_PROVIDER,
  trackingOffRouteLockKey,
  trackingOffRouteSinceKey,
} from './off-route.constants';
import { projectPointToRoute, type RouteGeometryPoint, type RouteGeometryProvider } from './route-geometry.provider';
import { RouteStateGenerationRegistry } from '../route-state/route-state-generation.registry';

interface OffRouteState {
  firstDetectedAt: string;
  alertedAt?: string;
  lastRealtimeEmittedAt?: string;
}

const MILLISECONDS_PER_SECOND = 1_000;
const RELEASE_LOCK_SCRIPT = `
if redis.call('GET', KEYS[1]) ~= ARGV[1] then return 0 end
return redis.call('DEL', KEYS[1])
`;

export type RouteDeviationStatus = 'DEVIATED' | 'ROUTE_RESTORED';

export interface TripRouteDeviationEvent {
  tripId: string;
  status: RouteDeviationStatus;
  distanceMeters: number;
  updatedAt: string;
}

export interface OffRouteAlertPayload {
  tripId: string;
  alertRecipientUserIds?: string[];
  latitude: number;
  longitude: number;
  distanceMeters: number;
  durationSeconds: number;
  detectedAt: string;
}

@Injectable()
export class OffRouteService {
  private readonly logger = pino({ name: OffRouteService.name });

  constructor(
    private readonly redis: RedisService,
    private readonly prisma: TrackingPrismaService,
    @Inject(ROUTE_GEOMETRY_PROVIDER) private readonly routeGeometryProvider: RouteGeometryProvider,
    private readonly routeStateGeneration: RouteStateGenerationRegistry,
  ) {}

  async handleGpsUpdate(gps: GpsUpdateEvent): Promise<TripRouteDeviationEvent | null> {
    try {
      return await this.evaluateGpsUpdate(gps);
    } catch (error) {
      this.logger.warn({ err: error, tripId: gps.tripId }, 'Skipping off-route detection after provider/calculation failure');
      return null;
    }
  }

  async clearRuntimeState(tripId: string): Promise<void> {
    const ownerToken = await this.acquireLock(tripId);
    if (!ownerToken) throw new Error(`OFF_ROUTE_STATE_LOCKED_${tripId}`);

    try {
      await this.redis.getClient().del(trackingOffRouteSinceKey(tripId));
    } finally {
      await this.releaseLock(tripId, ownerToken);
    }
  }

  private async evaluateGpsUpdate(gps: GpsUpdateEvent): Promise<TripRouteDeviationEvent | null> {
    const routeGeneration = this.routeStateGeneration.capture(gps.tripId);
    const route = this.routeGeometryProvider.peekCachedRouteGeometry(gps.tripId);
    if (!route) {
      void this.routeGeometryProvider.getRouteGeometry(gps.tripId).catch(() => null);
      return null;
    }
    if (route.points.length < 2) return null;

    const distanceMeters = Math.round(calculateNearestRouteDistanceMeters(gps, route.points));
    const ownerToken = await this.acquireLock(gps.tripId);
    if (!ownerToken) return null;

    try {
      return await this.evaluateLocked(gps, distanceMeters, routeGeneration, route.alertRecipientUserIds);
    } finally {
      await this.releaseLock(gps.tripId, ownerToken);
    }
  }

  private async evaluateLocked(
    gps: GpsUpdateEvent,
    distanceMeters: number,
    routeGeneration: number,
    alertRecipientUserIds?: string[],
  ): Promise<TripRouteDeviationEvent | null> {
    const stateKey = trackingOffRouteSinceKey(gps.tripId);
    const state = await this.readState(stateKey);
    if (!this.routeStateGeneration.isCurrent(gps.tripId, routeGeneration)) return null;

    if (distanceMeters <= OFF_ROUTE_DISTANCE_THRESHOLD_METERS) {
      if (!state) return null;
      await this.redis.getClient().del(stateKey);
      return state.alertedAt
        ? this.createRealtimeEvent(gps, distanceMeters, 'ROUTE_RESTORED')
        : null;
    }

    const detectedAtMs = new Date(gps.recordedAt).getTime();
    if (!state) {
      await this.writeState(stateKey, { firstDetectedAt: gps.recordedAt });
      return null;
    }

    if (state.alertedAt) {
      const lastRealtimeEmittedAtMs = new Date(
        state.lastRealtimeEmittedAt ?? state.alertedAt,
      ).getTime();
      if (detectedAtMs - lastRealtimeEmittedAtMs < OFF_ROUTE_REALTIME_HEARTBEAT_MS) return null;
      if (!this.routeStateGeneration.isCurrent(gps.tripId, routeGeneration)) return null;
      await this.writeState(stateKey, {
        ...state,
        lastRealtimeEmittedAt: gps.recordedAt,
      });
      return this.createRealtimeEvent(gps, distanceMeters, 'DEVIATED');
    }

    const firstDetectedAtMs = new Date(state.firstDetectedAt).getTime();
    if (detectedAtMs - firstDetectedAtMs <= OFF_ROUTE_CONTINUOUS_THRESHOLD_MS) return null;

    const payload: OffRouteAlertPayload = {
      tripId: gps.tripId,
      ...(alertRecipientUserIds?.length ? { alertRecipientUserIds } : {}),
      latitude: gps.latitude,
      longitude: gps.longitude,
      distanceMeters,
      durationSeconds: Math.floor((detectedAtMs - firstDetectedAtMs) / MILLISECONDS_PER_SECOND),
      detectedAt: gps.recordedAt,
    };
    if (!this.routeStateGeneration.isCurrent(gps.tripId, routeGeneration)) return null;
    await this.createOutboxEvent(payload, state.firstDetectedAt);
    if (!this.routeStateGeneration.isCurrent(gps.tripId, routeGeneration)) return null;
    await this.writeState(stateKey, {
      ...state,
      alertedAt: gps.recordedAt,
      lastRealtimeEmittedAt: gps.recordedAt,
    });
    return this.routeStateGeneration.isCurrent(gps.tripId, routeGeneration)
      ? this.createRealtimeEvent(gps, distanceMeters, 'DEVIATED')
      : null;
  }

  private async readState(key: string): Promise<OffRouteState | null> {
    const payload = await this.redis.getClient().get(key);
    if (!payload) return null;

    try {
      const parsed = JSON.parse(payload) as Partial<OffRouteState>;
      if (typeof parsed.firstDetectedAt !== 'string') return null;
      if (parsed.alertedAt !== undefined && typeof parsed.alertedAt !== 'string') return null;
      if (
        parsed.lastRealtimeEmittedAt !== undefined &&
        typeof parsed.lastRealtimeEmittedAt !== 'string'
      ) return null;
      return parsed as OffRouteState;
    } catch {
      return null;
    }
  }

  private async writeState(key: string, state: OffRouteState): Promise<void> {
    await this.redis.getClient().set(key, JSON.stringify(state), 'EX', OFF_ROUTE_STATE_TTL_SECONDS);
  }

  private async createOutboxEvent(
    payload: OffRouteAlertPayload,
    firstDetectedAt: string,
  ): Promise<void> {
    const dedupeKey = `off-route:${payload.tripId}:${firstDetectedAt}`;
    await this.prisma.outboxEvent.upsert({
      where: { dedupeKey },
      create: {
        eventType: OFF_ROUTE_EVENT_TYPE,
        dedupeKey,
        payload: {
          tripId: payload.tripId,
          ...(payload.alertRecipientUserIds?.length ? { userIds: payload.alertRecipientUserIds } : {}),
          latitude: payload.latitude,
          longitude: payload.longitude,
          distanceMeters: payload.distanceMeters,
          durationSeconds: payload.durationSeconds,
          detectedAt: payload.detectedAt,
        },
      },
      update: {},
    });
  }

  private createRealtimeEvent(
    gps: GpsUpdateEvent,
    distanceMeters: number,
    status: RouteDeviationStatus,
  ): TripRouteDeviationEvent {
    return {
      tripId: gps.tripId,
      status,
      distanceMeters,
      updatedAt: gps.recordedAt,
    };
  }

  private async acquireLock(tripId: string): Promise<string | null> {
    const ownerToken = randomUUID();
    const acquired = await this.redis.getClient().set(
      trackingOffRouteLockKey(tripId),
      ownerToken,
      'EX',
      OFF_ROUTE_LOCK_TTL_SECONDS,
      'NX',
    );
    return acquired === 'OK' ? ownerToken : null;
  }

  private async releaseLock(tripId: string, ownerToken: string): Promise<void> {
    await this.redis.getClient().eval(
      RELEASE_LOCK_SCRIPT,
      1,
      trackingOffRouteLockKey(tripId),
      ownerToken,
    );
  }
}

export function calculateNearestRouteDistanceMeters(
  point: RouteGeometryPoint,
  routePoints: RouteGeometryPoint[],
): number {
  return projectPointToRoute(point, routePoints)?.distanceMeters ?? Number.POSITIVE_INFINITY;
}
