import { Inject, Injectable } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import pino from 'pino';
import { randomUUID } from 'node:crypto';
import { trackingEtaKey } from '../location/location.constants';
import type { GpsUpdateEvent } from '../location/location.service';
import { ROUTE_GEOMETRY_PROVIDER } from '../off-route/off-route.constants';
import { projectPointToRoute, type RouteGeometryProvider } from '../off-route/route-geometry.provider';
import {
  EARTH_RADIUS_METERS,
  ETA_CACHE_TTL_SECONDS,
  ETA_FAILURE_COOLDOWN_SECONDS,
  ETA_LOCK_TTL_SECONDS,
  ETA_GOOGLE_FAILURE_THRESHOLD,
  ETA_MIN_INTERVAL_SECONDS,
  GOOGLE_ETA_PROVIDER,
  LOCAL_ETA_PROVIDER,
  ETA_RECALCULATE_DISTANCE_THRESHOLD_METERS,
  ETA_RECALCULATE_SOON_THRESHOLD_MINUTES,
  ETA_STATE_TTL_SECONDS,
  ETA_STOP_REACHED_DISTANCE_METERS,
  MILLISECONDS_PER_SECOND,
  SECONDS_PER_MINUTE,
  trackingEtaLockKey,
  trackingEtaStateKey,
  TRIP_DATA_PROVIDER,
} from './eta.constants';
import type { EtaProvider, EtaProviderResult } from './eta-provider';
import type { TripDataProvider, TripStopSnapshot } from './trip-data.provider';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';

export interface EtaUpdateEvent {
  tripId: string;
  stopId: string;
  etaMinutes: number;
  estimatedArrivalTime: string;
  distanceMeters: number;
  updatedAt: string;
}

interface EtaState {
  latitude?: number;
  longitude?: number;
  etaMinutes?: number;
  stopId?: string;
  stopSequence?: number;
  lastRouteProgressMeters?: number;
  lastProviderCallAt?: string;
  googleFailureCount: number;
  cooldownUntil?: string;
}
interface ProviderCalculation {
  result: EtaProviderResult | null;
  googleFailureCount: number;
  cooldownUntil?: string;
}

const COMPLETED_STOP_STATUSES = new Set([
  'COMPLETED',
  'ARRIVED',
  'SKIPPED',
  'PICKED_UP',
  'DROPPED_OFF',
  'CANCELLED',
]);
const RELEASE_LOCK_SCRIPT = `if redis.call('GET', KEYS[1]) == ARGV[1] then return redis.call('DEL', KEYS[1]) else return 0 end`;

@Injectable()
export class EtaService {
  private readonly logger = pino({ name: EtaService.name });

  constructor(
    private readonly redis: RedisService,
    @Inject(TRIP_DATA_PROVIDER) private readonly tripDataProvider: TripDataProvider,
    @Inject(ROUTE_GEOMETRY_PROVIDER) private readonly routeGeometryProvider: RouteGeometryProvider,
    @Inject(GOOGLE_ETA_PROVIDER) private readonly googleProvider: EtaProvider,
    @Inject(LOCAL_ETA_PROVIDER) private readonly localProvider: EtaProvider,
    @Inject(ENV_TOKEN) private readonly env: Env,
  ) {}

  async handleGpsUpdate(gps: GpsUpdateEvent): Promise<EtaUpdateEvent | null> {
    try {
      return await this.calculateEta(gps);
    } catch (error) {
      this.logger.warn({ err: error, tripId: gps.tripId }, 'Skipping ETA calculation after provider/calculation failure');
      return null;
    }
  }

  private async calculateEta(gps: GpsUpdateEvent): Promise<EtaUpdateEvent | null> {
    const stops = await this.tripDataProvider.getRouteStops(gps.tripId);
    const state = await this.readState(gps.tripId);
    const nextStop = this.findNextStop(stops, gps, state);
    if (!nextStop) return null;

    const cached = await this.readCachedEta(gps.tripId, nextStop.stopId);
    if (!this.shouldRecalculate(gps, state, cached, nextStop.stopId)) return null;
    const lockKey = trackingEtaLockKey(gps.tripId, nextStop.stopId);
    const owner = randomUUID();
    const acquired = await this.redis.getClient().set(lockKey, owner, 'EX', ETA_LOCK_TTL_SECONDS, 'NX');
    if (acquired !== 'OK') return null;

    try {
      const calculation = await this.calculateWithProviders(gps, nextStop, state);
      if (!calculation.result) {
        await this.redis.getClient().set(trackingEtaStateKey(gps.tripId), JSON.stringify({
          ...state,
          latitude: gps.latitude,
          longitude: gps.longitude,
          stopId: nextStop.stopId,
          stopSequence: nextStop.sequence,
          lastProviderCallAt: new Date().toISOString(),
          googleFailureCount: calculation.googleFailureCount,
          ...(calculation.cooldownUntil ? { cooldownUntil: calculation.cooldownUntil } : {}),
        }), 'EX', ETA_STATE_TTL_SECONDS);
        return null;
      }
      const result = calculation.result;
      const event: EtaUpdateEvent = {
        tripId: gps.tripId,
        stopId: nextStop.stopId,
        etaMinutes: result.etaMinutes,
        estimatedArrivalTime: new Date(new Date(gps.recordedAt).getTime() + result.etaMinutes * SECONDS_PER_MINUTE * MILLISECONDS_PER_SECOND).toISOString(),
        distanceMeters: result.distanceMeters,
        updatedAt: new Date().toISOString(),
      };
      const route = this.routeGeometryProvider.peekCachedRouteGeometry(gps.tripId);
      const progress = route ? projectPointToRoute(gps, route.points)?.progressMeters : undefined;
      const nextProgress = progress === undefined
        ? state?.lastRouteProgressMeters
        : Math.max(progress, state?.lastRouteProgressMeters ?? 0);
      const stateWithoutCooldown: EtaState = { ...(state ?? { googleFailureCount: 0 }) };
      delete stateWithoutCooldown.cooldownUntil;
      const nextState: EtaState = {
        ...stateWithoutCooldown,
        latitude: gps.latitude,
        longitude: gps.longitude,
        etaMinutes: event.etaMinutes,
        stopId: nextStop.stopId,
        stopSequence: nextStop.sequence,
        ...(nextProgress !== undefined ? { lastRouteProgressMeters: nextProgress } : {}),
        lastProviderCallAt: new Date().toISOString(),
        googleFailureCount: calculation.googleFailureCount,
        ...(calculation.cooldownUntil ? { cooldownUntil: calculation.cooldownUntil } : {}),
      };
      const etaCacheTtl = this.env.TRACKING_ETA_CACHE_TTL_SECONDS ?? ETA_CACHE_TTL_SECONDS;
      await this.redis.getClient().multi()
        .set(trackingEtaKey(gps.tripId, nextStop.stopId), JSON.stringify(event), 'EX', etaCacheTtl)
        .set(trackingEtaStateKey(gps.tripId), JSON.stringify(nextState), 'EX', ETA_STATE_TTL_SECONDS)
        .exec();
      return event;
    } finally {
      try {
        await this.redis.getClient().eval(RELEASE_LOCK_SCRIPT, 1, lockKey, owner);
      } catch (error) {
        this.logger.warn({ err: error, tripId: gps.tripId, stopId: nextStop.stopId }, 'Failed to release ETA lock');
      }
    }
  }

  private async calculateWithProviders(gps: GpsUpdateEvent, stop: TripStopSnapshot, state: EtaState | null): Promise<ProviderCalculation> {
    const googleEnabled = this.env.GOOGLE_ROUTES_ENABLED === true && Boolean(this.env.GOOGLE_ROUTES_API_KEY);
    const cooldownActive = Boolean(state?.cooldownUntil && new Date(state.cooldownUntil).getTime() > Date.now());
    let googleFailureCount = state?.googleFailureCount ?? 0;
    let cooldownUntil = state?.cooldownUntil;
    if (googleEnabled && !cooldownActive) {
      const googleResult = await this.googleProvider.calculate(gps, stop);
      if (googleResult) return { result: googleResult, googleFailureCount: 0 };
      googleFailureCount += 1;
      if (googleFailureCount >= ETA_GOOGLE_FAILURE_THRESHOLD) {
        cooldownUntil = new Date(Date.now() + (this.env.TRACKING_ETA_FAILURE_COOLDOWN_SECONDS ?? ETA_FAILURE_COOLDOWN_SECONDS) * 1000).toISOString();
      }
    }
    return {
      result: await this.localProvider.calculate(gps, stop),
      googleFailureCount,
      ...(cooldownUntil ? { cooldownUntil } : {}),
    };
  }

  private findNextStop(stops: TripStopSnapshot[], gps: GpsUpdateEvent, state: EtaState | null): TripStopSnapshot | null {
    const sorted = [...stops].sort((left, right) => left.sequence - right.sequence);
    const route = this.routeGeometryProvider.peekCachedRouteGeometry(gps.tripId);
    const vehicleProjection = route ? projectPointToRoute(gps, route.points) : null;
    const currentProgress = Math.max(vehicleProjection?.progressMeters ?? 0, state?.lastRouteProgressMeters ?? 0);
    return sorted.find((stop) => {
      if (stop.status && COMPLETED_STOP_STATUSES.has(stop.status)) return false;
      if (state?.stopSequence !== undefined && stop.sequence < state.stopSequence) return false;
      if (calculateDistanceMeters(gps.latitude, gps.longitude, stop.latitude, stop.longitude) <= ETA_STOP_REACHED_DISTANCE_METERS) return false;
      const projection = route ? projectPointToRoute(stop, route.points) : null;
      if (projection && projection.progressMeters <= currentProgress) return false;
      return true;
    }) ?? null;
  }

  private async readState(tripId: string): Promise<EtaState | null> {
    const payload = await this.redis.getClient().get(trackingEtaStateKey(tripId));
    if (!payload) return null;
    try {
      const parsed = JSON.parse(payload) as Partial<EtaState>;
      if (typeof parsed.googleFailureCount !== 'number') parsed.googleFailureCount = 0;
      return parsed as EtaState;
    } catch {
      return null;
    }
  }

  private async readCachedEta(tripId: string, stopId: string): Promise<EtaUpdateEvent | null> {
    const payload = await this.redis.getClient().get(trackingEtaKey(tripId, stopId));
    if (!payload) return null;
    try {
      const parsed = JSON.parse(payload) as EtaUpdateEvent;
      return parsed.tripId === tripId && parsed.stopId === stopId && Number.isFinite(parsed.etaMinutes) ? parsed : null;
    } catch {
      return null;
    }
  }

  private shouldRecalculate(
    gps: GpsUpdateEvent,
    state: EtaState | null,
    cached: EtaUpdateEvent | null,
    selectedStopId: string,
  ): boolean {
    if (!state?.lastProviderCallAt) return true;
    if (!cached && state.stopId !== selectedStopId) return true;
    const interval = this.env.TRACKING_ETA_MIN_INTERVAL_SECONDS ?? ETA_MIN_INTERVAL_SECONDS;
    if (Date.now() - new Date(state.lastProviderCallAt).getTime() < interval * 1000) return false;
    if (!cached) return true;
    if ((cached.etaMinutes ?? state.etaMinutes ?? Number.POSITIVE_INFINITY) < ETA_RECALCULATE_SOON_THRESHOLD_MINUTES) return true;
    if (state.latitude === undefined || state.longitude === undefined) return true;
    return calculateDistanceMeters(gps.latitude, gps.longitude, state.latitude, state.longitude) > ETA_RECALCULATE_DISTANCE_THRESHOLD_METERS;
  }
}

export function calculateDistanceMeters(latitudeA: number, longitudeA: number, latitudeB: number, longitudeB: number): number {
  const latARadians = degreesToRadians(latitudeA);
  const latBRadians = degreesToRadians(latitudeB);
  const deltaLat = degreesToRadians(latitudeB - latitudeA);
  const deltaLon = degreesToRadians(longitudeB - longitudeA);
  const haversine = Math.sin(deltaLat / 2) ** 2 + Math.cos(latARadians) * Math.cos(latBRadians) * Math.sin(deltaLon / 2) ** 2;
  return EARTH_RADIUS_METERS * 2 * Math.atan2(Math.sqrt(haversine), Math.sqrt(1 - haversine));
}

function degreesToRadians(value: number): number {
  return (value * Math.PI) / 180;
}
