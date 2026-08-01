import { Inject, Injectable } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import pino from 'pino';
import type { GpsUpdateEvent } from '../location/location.service';
import { TrackingPrismaService } from '../prisma/tracking-prisma.service';
import {
  OFF_ROUTE_CONTINUOUS_THRESHOLD_MS,
  OFF_ROUTE_DISTANCE_THRESHOLD_METERS,
  OFF_ROUTE_EVENT_TYPE,
  OFF_ROUTE_STATE_TTL_SECONDS,
  ROUTE_GEOMETRY_PROVIDER,
  trackingOffRouteSinceKey,
} from './off-route.constants';
import { projectPointToRoute, type RouteGeometryPoint, type RouteGeometryProvider } from './route-geometry.provider';

interface OffRouteState {
  firstDetectedAt: string;
  alertedAt?: string;
}

const MILLISECONDS_PER_SECOND = 1_000;

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
  ) {}

  async handleGpsUpdate(gps: GpsUpdateEvent): Promise<OffRouteAlertPayload | null> {
    try {
      return await this.evaluateGpsUpdate(gps);
    } catch (error) {
      this.logger.warn({ err: error, tripId: gps.tripId }, 'Skipping off-route detection after provider/calculation failure');
      return null;
    }
  }

  private async evaluateGpsUpdate(gps: GpsUpdateEvent): Promise<OffRouteAlertPayload | null> {
    const route = this.routeGeometryProvider.peekCachedRouteGeometry(gps.tripId);
    if (!route) {
      void this.routeGeometryProvider.getRouteGeometry(gps.tripId).catch(() => null);
      return null;
    }
    if (route.points.length < 2) return null;

    const distanceMeters = Math.round(calculateNearestRouteDistanceMeters(gps, route.points));
    const stateKey = trackingOffRouteSinceKey(gps.tripId);
    if (distanceMeters <= OFF_ROUTE_DISTANCE_THRESHOLD_METERS) {
      await this.redis.getClient().del(stateKey);
      return null;
    }

    const detectedAtMs = new Date(gps.recordedAt).getTime();
    const state = await this.readState(stateKey);
    if (!state) {
      await this.writeInitialState(stateKey, { firstDetectedAt: gps.recordedAt });
      return null;
    }

    if (state.alertedAt) return null;

    const firstDetectedAtMs = new Date(state.firstDetectedAt).getTime();
    if (detectedAtMs - firstDetectedAtMs <= OFF_ROUTE_CONTINUOUS_THRESHOLD_MS) return null;

    const payload: OffRouteAlertPayload = {
      tripId: gps.tripId,
      ...(route.alertRecipientUserIds?.length ? { alertRecipientUserIds: route.alertRecipientUserIds } : {}),
      latitude: gps.latitude,
      longitude: gps.longitude,
      distanceMeters,
      durationSeconds: Math.floor((detectedAtMs - firstDetectedAtMs) / MILLISECONDS_PER_SECOND),
      detectedAt: gps.recordedAt,
    };
    await this.createOutboxEvent(payload);
    await this.writeState(stateKey, { ...state, alertedAt: gps.recordedAt });
    return payload;
  }

  private async readState(key: string): Promise<OffRouteState | null> {
    const payload = await this.redis.getClient().get(key);
    if (!payload) return null;

    try {
      const parsed = JSON.parse(payload) as Partial<OffRouteState>;
      if (typeof parsed.firstDetectedAt !== 'string') return null;
      if (parsed.alertedAt !== undefined && typeof parsed.alertedAt !== 'string') return null;
      return parsed as OffRouteState;
    } catch {
      return null;
    }
  }

  private async writeState(key: string, state: OffRouteState): Promise<void> {
    await this.redis.getClient().set(key, JSON.stringify(state), 'EX', OFF_ROUTE_STATE_TTL_SECONDS);
  }

  private async writeInitialState(key: string, state: OffRouteState): Promise<void> {
    await this.redis.getClient().set(key, JSON.stringify(state), 'EX', OFF_ROUTE_STATE_TTL_SECONDS, 'NX');
  }

  private async createOutboxEvent(payload: OffRouteAlertPayload): Promise<void> {
    await this.prisma.outboxEvent.create({
      data: {
        eventType: OFF_ROUTE_EVENT_TYPE,
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
    });
  }
}

export function calculateNearestRouteDistanceMeters(
  point: RouteGeometryPoint,
  routePoints: RouteGeometryPoint[],
): number {
  return projectPointToRoute(point, routePoints)?.distanceMeters ?? Number.POSITIVE_INFINITY;
}
