import { Inject, Injectable } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import pino from 'pino';
import { randomUUID } from 'node:crypto';
import { trackingEtaKey } from '../location/location.constants';
import type { GpsUpdateEvent } from '../location/location.service';
import { ROUTE_GEOMETRY_PROVIDER } from '../off-route/off-route.constants';
import { projectPointToRoute, type RouteGeometryProvider } from '../off-route/route-geometry.provider';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import {
  EARTH_RADIUS_METERS,
  ETA_CACHE_TTL_SECONDS,
  ETA_DEFAULT_SPEED_KMH,
  ETA_FAILURE_COOLDOWN_SECONDS,
  ETA_GOOGLE_FAILURE_THRESHOLD,
  ETA_LOCK_TTL_SECONDS,
  ETA_MIN_INTERVAL_SECONDS,
  ETA_MIN_SPEED_KMH,
  ETA_RECALCULATE_DISTANCE_THRESHOLD_METERS,
  ETA_RECALCULATE_SOON_THRESHOLD_MINUTES,
  ETA_STATE_TTL_SECONDS,
  ETA_STOP_REACHED_DISTANCE_METERS,
  GOOGLE_ETA_PROVIDER,
  LOCAL_ETA_PROVIDER,
  METERS_PER_KILOMETER,
  MILLISECONDS_PER_SECOND,
  SECONDS_PER_HOUR,
  SECONDS_PER_MINUTE,
  trackingEtaBatchLockKey,
  trackingEtaStateKey,
  TRIP_DATA_PROVIDER,
} from './eta.constants';
import type { EtaBatchTargetResult, EtaProvider } from './eta-provider';
import type { TripDataProvider, TripStopSnapshot } from './trip-data.provider';
import { RouteStateGenerationRegistry } from '../route-state/route-state-generation.registry';

export type EstimateQuality = 'TRAFFIC_AWARE' | 'FALLBACK';

export interface EtaTargetUpdateEvent {
  tripId: string;
  targetKind: 'STOP' | 'STATION';
  stopId?: string;
  stationId?: string;
  stopName: string | null;
  sequence?: number;
  etaMinutes: number;
  estimatedArrivalTime: string;
  distanceMeters: number;
  updatedAt: string;
  estimateQuality: EstimateQuality;
}

export interface EtaUpdateEvent {
  tripId: string;
  stopId: string;
  stationId?: string;
  stopName?: string | null;
  targetKind?: 'STOP' | 'STATION';
  etaMinutes: number;
  estimatedArrivalTime: string;
  distanceMeters: number;
  updatedAt: string;
  estimateQuality?: EstimateQuality;
  etas?: EtaTargetUpdateEvent[];
}

interface EtaState {
  latitude?: number;
  longitude?: number;
  etaMinutes?: number;
  targetKind?: 'STOP' | 'STATION';
  targetId?: string;
  targetSequence?: number;
  // Read-only rollout compatibility for state written by the previous version.
  stopId?: string;
  stopSequence?: number;
  lastRouteProgressMeters?: number;
  lastProviderCallAt?: string;
  googleFailureCount: number;
  cooldownUntil?: string;
}

interface BatchProviderCalculation {
  results: EtaBatchTargetResult[] | null;
  estimateQuality: EstimateQuality;
  googleFailureCount: number;
  cooldownUntil?: string;
}

type EtaCalculationTarget = TripStopSnapshot & {
  targetKind: 'STOP' | 'STATION';
};

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
    private readonly routeStateGeneration: RouteStateGenerationRegistry,
  ) {}

  async handleGpsUpdate(gps: GpsUpdateEvent): Promise<EtaUpdateEvent | null> {
    try {
      return await this.calculateEta(gps);
    } catch (error) {
      this.logger.warn(
        { err: error, tripId: gps.tripId },
        'Skipping ETA calculation after provider/calculation failure',
      );
      return null;
    }
  }

  private async calculateEta(gps: GpsUpdateEvent): Promise<EtaUpdateEvent | null> {
    const routeGeneration = this.routeStateGeneration.capture(gps.tripId);
    const stops = await this.tripDataProvider.getRouteStops(gps.tripId);
    if (!this.routeStateGeneration.isCurrent(gps.tripId, routeGeneration)) return null;
    const state = await this.readState(gps.tripId);
    const route = this.routeGeometryProvider.peekCachedRouteGeometry(gps.tripId)
      ?? await this.routeGeometryProvider.getRouteGeometry(gps.tripId);
    if (!this.routeStateGeneration.isCurrent(gps.tripId, routeGeneration)) return null;

    const remainingStops = this.findRemainingStops(stops, gps, state);
    const tripStatus = route?.tripStatus?.toUpperCase() ?? 'IN_PROGRESS';
    const preOrigin = tripStatus === 'SCHEDULED' || tripStatus === 'BOARDING';
    const targets: EtaCalculationTarget[] = preOrigin
      ? route?.originStation
        ? [{
            stopId: route.originStation.stationId,
            stopName: route.originStation.name,
            latitude: route.originStation.latitude,
            longitude: route.originStation.longitude,
            sequence: 0,
            targetKind: 'STATION',
          }]
        : []
      : tripStatus === 'IN_PROGRESS'
        ? [
            ...remainingStops.map((stop) => ({ ...stop, targetKind: 'STOP' as const })),
            ...(route?.destinationStation
              ? [{
                  stopId: route.destinationStation.stationId,
                  stopName: route.destinationStation.name,
                  latitude: route.destinationStation.latitude,
                  longitude: route.destinationStation.longitude,
                  sequence: Number.MAX_SAFE_INTEGER,
                  targetKind: 'STATION' as const,
                }]
              : []),
          ]
        : [];
    const primaryTarget = targets[0];
    if (!primaryTarget) return null;

    const cached = await this.readCachedEta(gps.tripId, primaryTarget);
    if (!this.shouldRecalculate(gps, state, cached, primaryTarget)) return null;
    const lockKey = trackingEtaBatchLockKey(gps.tripId);
    const owner = randomUUID();
    const acquired = await this.redis.getClient().set(
      lockKey,
      owner,
      'EX',
      ETA_LOCK_TTL_SECONDS,
      'NX',
    );
    if (acquired !== 'OK') return null;

    try {
      const calculation = await this.calculateBatchWithProviders(
        gps,
        targets,
        state,
        preOrigin,
      );
      if (!this.routeStateGeneration.isCurrent(gps.tripId, routeGeneration)) return null;
      if (!calculation.results) {
        await this.writeFailureState(gps, primaryTarget, state, calculation, routeGeneration);
        return null;
      }

      const updatedAt = new Date().toISOString();
      const baseTime = Date.now();
      const resultById = new Map(calculation.results.map((result) => [result.targetId, result]));
      const events: EtaTargetUpdateEvent[] = [];
      for (const target of targets) {
        const result = resultById.get(target.stopId);
        if (!result) return null;
        events.push({
          tripId: gps.tripId,
          targetKind: target.targetKind,
          ...(target.targetKind === 'STATION'
            ? { stationId: target.stopId }
            : { stopId: target.stopId }),
          stopName: target.stopName ?? null,
          ...(target.targetKind === 'STOP' ? { sequence: target.sequence } : {}),
          etaMinutes: result.etaMinutes,
          estimatedArrivalTime: new Date(
            baseTime + result.etaMinutes * SECONDS_PER_MINUTE * MILLISECONDS_PER_SECOND,
          ).toISOString(),
          distanceMeters: result.distanceMeters,
          updatedAt,
          estimateQuality: calculation.estimateQuality,
        });
      }

      const primaryEvent = events[0];
      if (!primaryEvent) return null;
      const primaryTargetId = primaryEvent.targetKind === 'STOP'
        ? primaryEvent.stopId
        : primaryEvent.stationId;
      if (!primaryTargetId) return null;

      const routeProgress = !preOrigin && route
        ? projectPointToRoute(gps, route.points)?.progressMeters
        : undefined;
      const nextProgress = routeProgress === undefined
        ? state?.lastRouteProgressMeters
        : Math.max(routeProgress, state?.lastRouteProgressMeters ?? 0);
      const stateWithoutCooldown: EtaState = { ...(state ?? { googleFailureCount: 0 }) };
      delete stateWithoutCooldown.cooldownUntil;
      delete stateWithoutCooldown.stopId;
      delete stateWithoutCooldown.stopSequence;
      delete stateWithoutCooldown.targetSequence;
      const nextState: EtaState = {
        ...stateWithoutCooldown,
        latitude: gps.latitude,
        longitude: gps.longitude,
        etaMinutes: primaryEvent.etaMinutes,
        targetKind: primaryEvent.targetKind,
        targetId: primaryTargetId,
        ...(primaryEvent.sequence !== undefined
          ? { targetSequence: primaryEvent.sequence }
          : {}),
        ...(nextProgress !== undefined ? { lastRouteProgressMeters: nextProgress } : {}),
        lastProviderCallAt: updatedAt,
        googleFailureCount: calculation.googleFailureCount,
        ...(calculation.cooldownUntil ? { cooldownUntil: calculation.cooldownUntil } : {}),
      };
      const etaCacheTtl = this.env.TRACKING_ETA_CACHE_TTL_SECONDS ?? ETA_CACHE_TTL_SECONDS;
      if (!this.routeStateGeneration.isCurrent(gps.tripId, routeGeneration)) return null;
      const transaction = this.redis.getClient().multi();
      const activeTargetIds = new Set(events.flatMap((event) => {
        const targetId = event.targetKind === 'STOP' ? event.stopId : event.stationId;
        return targetId ? [targetId] : [];
      }));
      const previousTargetId = state?.targetId ?? state?.stopId;
      if (previousTargetId && !activeTargetIds.has(previousTargetId)) {
        transaction.del(trackingEtaKey(gps.tripId, previousTargetId));
      }
      for (const event of events) {
        const targetId = event.targetKind === 'STOP' ? event.stopId : event.stationId;
        if (targetId) {
          transaction.set(
            trackingEtaKey(gps.tripId, targetId),
            JSON.stringify(event),
            'EX',
            etaCacheTtl,
          );
        }
      }
      transaction.set(
        trackingEtaStateKey(gps.tripId),
        JSON.stringify(nextState),
        'EX',
        ETA_STATE_TTL_SECONDS,
      );
      await transaction.exec();
      if (!this.routeStateGeneration.isCurrent(gps.tripId, routeGeneration)) {
        await this.redis.getClient().del(
          trackingEtaStateKey(gps.tripId),
          ...events.flatMap((event) => {
            const targetId = event.targetKind === 'STOP' ? event.stopId : event.stationId;
            return targetId ? [trackingEtaKey(gps.tripId, targetId)] : [];
          }),
        );
        return null;
      }

      return { ...primaryEvent, stopId: primaryTargetId, etas: events };
    } finally {
      try {
        await this.redis.getClient().eval(RELEASE_LOCK_SCRIPT, 1, lockKey, owner);
      } catch (error) {
        this.logger.warn({ err: error, tripId: gps.tripId }, 'Failed to release ETA batch lock');
      }
    }
  }

  private async calculateBatchWithProviders(
    gps: GpsUpdateEvent,
    targets: EtaCalculationTarget[],
    state: EtaState | null,
    directLocalFallback: boolean,
  ): Promise<BatchProviderCalculation> {
    const googleEnabled = this.env.GOOGLE_ROUTES_ENABLED === true
      && Boolean(this.env.GOOGLE_ROUTES_API_KEY);
    const cooldownActive = Boolean(
      state?.cooldownUntil && new Date(state.cooldownUntil).getTime() > Date.now(),
    );
    let googleFailureCount = state?.googleFailureCount ?? 0;
    let cooldownUntil = state?.cooldownUntil;
    if (googleEnabled && !cooldownActive) {
      const googleResults = await this.calculateProviderBatch(this.googleProvider, gps, targets);
      if (googleResults) {
        return {
          results: googleResults,
          estimateQuality: 'TRAFFIC_AWARE',
          googleFailureCount: 0,
        };
      }
      googleFailureCount += 1;
      if (googleFailureCount >= ETA_GOOGLE_FAILURE_THRESHOLD) {
        cooldownUntil = new Date(
          Date.now()
            + (this.env.TRACKING_ETA_FAILURE_COOLDOWN_SECONDS ?? ETA_FAILURE_COOLDOWN_SECONDS)
              * MILLISECONDS_PER_SECOND,
        ).toISOString();
      }
    }

    const localResults = directLocalFallback
      ? this.calculateDirectFallback(gps, targets)
      : await this.calculateProviderBatch(this.localProvider, gps, targets)
        ?? this.calculateDirectFallback(gps, targets);
    return {
      results: localResults,
      estimateQuality: 'FALLBACK',
      googleFailureCount,
      ...(cooldownUntil ? { cooldownUntil } : {}),
    };
  }

  private async calculateProviderBatch(
    provider: EtaProvider,
    gps: GpsUpdateEvent,
    targets: EtaCalculationTarget[],
  ): Promise<EtaBatchTargetResult[] | null> {
    try {
      if (provider.calculateBatch) return await provider.calculateBatch(gps, targets);
      const results: EtaBatchTargetResult[] = [];
      const dwellMinutes = this.env.TRIP_STOP_DWELL_MINUTES ?? 20;
      for (let index = 0; index < targets.length; index += 1) {
        const target = targets[index];
        if (!target) return null;
        const result = await provider.calculate(gps, target);
        if (!result) return null;
        results.push({
          targetId: target.stopId,
          distanceMeters: result.distanceMeters,
          etaMinutes: result.etaMinutes + dwellMinutes * index,
        });
      }
      return results;
    } catch (error) {
      this.logger.warn({ err: error, tripId: gps.tripId }, 'ETA batch provider failed');
      return null;
    }
  }

  private calculateDirectFallback(
    gps: GpsUpdateEvent,
    targets: EtaCalculationTarget[],
  ): EtaBatchTargetResult[] {
    const speedKmh = Math.max(gps.speedKmh ?? ETA_DEFAULT_SPEED_KMH, ETA_MIN_SPEED_KMH);
    const metersPerSecond = speedKmh * METERS_PER_KILOMETER / SECONDS_PER_HOUR;
    const dwellMinutes = this.env.TRIP_STOP_DWELL_MINUTES ?? 20;
    let latitude = gps.latitude;
    let longitude = gps.longitude;
    let cumulativeDistance = 0;
    return targets.map((target, index) => {
      cumulativeDistance += calculateDistanceMeters(
        latitude,
        longitude,
        target.latitude,
        target.longitude,
      );
      latitude = target.latitude;
      longitude = target.longitude;
      return {
        targetId: target.stopId,
        distanceMeters: Math.round(cumulativeDistance),
        etaMinutes: Math.max(
          1,
          Math.ceil(cumulativeDistance / metersPerSecond / SECONDS_PER_MINUTE)
            + dwellMinutes * index,
        ),
      };
    });
  }

  private findRemainingStops(
    stops: TripStopSnapshot[],
    gps: GpsUpdateEvent,
    state: EtaState | null,
  ): TripStopSnapshot[] {
    const sorted = [...stops].sort((left, right) => left.sequence - right.sequence);
    const route = this.routeGeometryProvider.peekCachedRouteGeometry(gps.tripId);
    const vehicleProjection = route ? projectPointToRoute(gps, route.points) : null;
    const currentProgress = Math.max(
      vehicleProjection?.progressMeters ?? 0,
      state?.lastRouteProgressMeters ?? 0,
    );
    return sorted.filter((stop) => {
      if (stop.status && COMPLETED_STOP_STATUSES.has(stop.status)) return false;
      const selectedSequence = state?.targetSequence ?? state?.stopSequence;
      if (selectedSequence !== undefined && stop.sequence < selectedSequence) return false;
      if (calculateDistanceMeters(
        gps.latitude,
        gps.longitude,
        stop.latitude,
        stop.longitude,
      ) <= ETA_STOP_REACHED_DISTANCE_METERS) return false;
      const projection = route ? projectPointToRoute(stop, route.points) : null;
      if (projection && projection.progressMeters <= currentProgress) return false;
      return true;
    });
  }

  private async writeFailureState(
    gps: GpsUpdateEvent,
    primaryTarget: EtaCalculationTarget,
    state: EtaState | null,
    calculation: BatchProviderCalculation,
    routeGeneration: number,
  ): Promise<void> {
    if (!this.routeStateGeneration.isCurrent(gps.tripId, routeGeneration)) return;
    const stateKey = trackingEtaStateKey(gps.tripId);
    const nextState: EtaState = { ...(state ?? { googleFailureCount: 0 }) };
    delete nextState.stopId;
    delete nextState.stopSequence;
    delete nextState.targetSequence;
    await this.redis.getClient().set(
      stateKey,
      JSON.stringify({
        ...nextState,
        latitude: gps.latitude,
        longitude: gps.longitude,
        targetKind: primaryTarget.targetKind,
        targetId: primaryTarget.stopId,
        ...(primaryTarget.targetKind === 'STOP'
          ? { targetSequence: primaryTarget.sequence }
          : {}),
        lastProviderCallAt: new Date().toISOString(),
        googleFailureCount: calculation.googleFailureCount,
        ...(calculation.cooldownUntil ? { cooldownUntil: calculation.cooldownUntil } : {}),
      }),
      'EX',
      ETA_STATE_TTL_SECONDS,
    );
    if (!this.routeStateGeneration.isCurrent(gps.tripId, routeGeneration)) {
      await this.redis.getClient().del(stateKey);
    }
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

  private async readCachedEta(
    tripId: string,
    target: EtaCalculationTarget,
  ): Promise<EtaTargetUpdateEvent | null> {
    const payload = await this.redis.getClient().get(trackingEtaKey(tripId, target.stopId));
    if (!payload) return null;
    try {
      const parsed = JSON.parse(payload) as EtaTargetUpdateEvent;
      const parsedTargetKind = parsed.targetKind ?? 'STOP';
      return parsed.tripId === tripId
        && parsedTargetKind === target.targetKind
        && (parsedTargetKind === 'STOP' ? parsed.stopId : parsed.stationId) === target.stopId
        && Number.isFinite(parsed.etaMinutes)
        ? parsed
        : null;
    } catch {
      return null;
    }
  }

  private shouldRecalculate(
    gps: GpsUpdateEvent,
    state: EtaState | null,
    cached: EtaTargetUpdateEvent | null,
    selectedTarget: EtaCalculationTarget,
  ): boolean {
    if (!state?.lastProviderCallAt) return true;
    const stateTargetId = state.targetId ?? state.stopId;
    const stateTargetKind = state.targetKind ?? (state.stopId ? 'STOP' : undefined);
    if (stateTargetId !== selectedTarget.stopId
      || stateTargetKind !== selectedTarget.targetKind) return true;
    const interval = this.env.TRACKING_ETA_MIN_INTERVAL_SECONDS ?? ETA_MIN_INTERVAL_SECONDS;
    if (Date.now() - new Date(state.lastProviderCallAt).getTime() < interval * 1_000) return false;
    if (!cached) return true;
    if ((cached.etaMinutes ?? state.etaMinutes ?? Number.POSITIVE_INFINITY)
      < ETA_RECALCULATE_SOON_THRESHOLD_MINUTES) return true;
    if (state.latitude === undefined || state.longitude === undefined) return true;
    return calculateDistanceMeters(
      gps.latitude,
      gps.longitude,
      state.latitude,
      state.longitude,
    ) > ETA_RECALCULATE_DISTANCE_THRESHOLD_METERS;
  }
}

export function calculateDistanceMeters(
  latitudeA: number,
  longitudeA: number,
  latitudeB: number,
  longitudeB: number,
): number {
  const latARadians = degreesToRadians(latitudeA);
  const latBRadians = degreesToRadians(latitudeB);
  const deltaLat = degreesToRadians(latitudeB - latitudeA);
  const deltaLon = degreesToRadians(longitudeB - longitudeA);
  const haversine = Math.sin(deltaLat / 2) ** 2
    + Math.cos(latARadians)
      * Math.cos(latBRadians)
      * Math.sin(deltaLon / 2) ** 2;
  return EARTH_RADIUS_METERS
    * 2
    * Math.atan2(Math.sqrt(haversine), Math.sqrt(1 - haversine));
}

function degreesToRadians(value: number): number {
  return value * Math.PI / 180;
}
