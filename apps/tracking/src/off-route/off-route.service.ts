import { Inject, Injectable } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import pino from 'pino';
import { calculateDistanceMeters } from '../eta/eta.service';
import type { GpsUpdateEvent } from '../location/location.service';
import { TrackingPrismaService } from '../prisma/tracking-prisma.service';
import {
  OFF_ROUTE_CONTINUOUS_THRESHOLD_MS,
  OFF_ROUTE_DISTANCE_THRESHOLD_METERS,
  OFF_ROUTE_EVENT_TYPE,
  OFF_ROUTE_STATE_TTL_SECONDS,
  ROUTE_GEOMETRY_PROVIDER,
  APPROXIMATE_METERS_PER_DEGREE_LATITUDE,
  trackingOffRouteSinceKey,
} from './off-route.constants';
import type { RouteGeometryPoint, RouteGeometryProvider } from './route-geometry.provider';

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
    const route = await this.routeGeometryProvider.getRouteGeometry(gps.tripId);
    if (!route || route.points.length < 2) return null;

    const distanceMeters = Math.round(calculateNearestRouteDistanceMeters(gps, route.points));
    const stateKey = trackingOffRouteSinceKey(gps.tripId);
    if (distanceMeters <= OFF_ROUTE_DISTANCE_THRESHOLD_METERS) {
      await this.redis.getClient().del(stateKey);
      return null;
    }

    const detectedAtMs = new Date(gps.recordedAt).getTime();
    const state = await this.readState(stateKey);
    if (!state) {
      await this.writeState(stateKey, { firstDetectedAt: gps.recordedAt });
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
  let nearestDistanceMeters = Number.POSITIVE_INFINITY;

  for (let index = 0; index < routePoints.length - 1; index += 1) {
    const segmentStart = routePoints[index];
    const segmentEnd = routePoints[index + 1];
    if (!segmentStart || !segmentEnd) continue;

    const distanceMeters = calculatePointToSegmentDistanceMeters(point, segmentStart, segmentEnd);
    nearestDistanceMeters = Math.min(nearestDistanceMeters, distanceMeters);
  }

  return nearestDistanceMeters;
}

function calculatePointToSegmentDistanceMeters(
  point: RouteGeometryPoint,
  segmentStart: RouteGeometryPoint,
  segmentEnd: RouteGeometryPoint,
): number {
  const segmentDistanceMeters = calculateDistanceMeters(
    segmentStart.latitude,
    segmentStart.longitude,
    segmentEnd.latitude,
    segmentEnd.longitude,
  );
  if (segmentDistanceMeters === 0) {
    return calculateDistanceMeters(point.latitude, point.longitude, segmentStart.latitude, segmentStart.longitude);
  }

  const projection = projectToLocalMeters(point, segmentStart, segmentEnd);
  const segmentLengthSquared = projection.segmentX * projection.segmentX + projection.segmentY * projection.segmentY;
  const rawPosition =
    (projection.pointX * projection.segmentX + projection.pointY * projection.segmentY) / segmentLengthSquared;
  const position = Math.max(0, Math.min(1, rawPosition));
  const nearestX = projection.segmentX * position;
  const nearestY = projection.segmentY * position;
  const deltaX = projection.pointX - nearestX;
  const deltaY = projection.pointY - nearestY;
  return Math.sqrt(deltaX * deltaX + deltaY * deltaY);
}

function projectToLocalMeters(
  point: RouteGeometryPoint,
  segmentStart: RouteGeometryPoint,
  segmentEnd: RouteGeometryPoint,
): {
  pointX: number;
  pointY: number;
  segmentX: number;
  segmentY: number;
} {
  const referenceLatitudeRadians = degreesToRadians(segmentStart.latitude);
  const metersPerDegreeLatitude = APPROXIMATE_METERS_PER_DEGREE_LATITUDE;
  const metersPerDegreeLongitude = metersPerDegreeLatitude * Math.cos(referenceLatitudeRadians);
  return {
    pointX: (point.longitude - segmentStart.longitude) * metersPerDegreeLongitude,
    pointY: (point.latitude - segmentStart.latitude) * metersPerDegreeLatitude,
    segmentX: (segmentEnd.longitude - segmentStart.longitude) * metersPerDegreeLongitude,
    segmentY: (segmentEnd.latitude - segmentStart.latitude) * metersPerDegreeLatitude,
  };
}

function degreesToRadians(value: number): number {
  return (value * Math.PI) / 180;
}
