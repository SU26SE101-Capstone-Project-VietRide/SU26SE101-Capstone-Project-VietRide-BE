import { Inject, Injectable } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import pino from 'pino';
import { randomUUID } from 'node:crypto';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import {
  ETA_GOOGLE_FAILURE_THRESHOLD,
  ETA_RECALCULATE_DISTANCE_THRESHOLD_METERS,
  ETA_RECALCULATE_SOON_THRESHOLD_MINUTES,
  METERS_PER_KILOMETER,
  MILLISECONDS_PER_SECOND,
  SECONDS_PER_MINUTE,
} from '../eta/eta.constants';
import { GoogleRoutesEtaProvider } from '../eta/google-routes-eta.provider';
import type { EtaProviderResult } from '../eta/eta-provider';
import { calculateDistanceMeters } from '../eta/eta.service';
import {
  SHUTTLE_ETA_LOCK_TTL_SECONDS,
  SHUTTLE_ETA_STATE_TTL_SECONDS,
  SHUTTLE_ETA_TTL_SECONDS,
  shuttleEtaKey,
  shuttleEtaLockKey,
  shuttleEtaStateKey,
} from './shuttle.constants';
import type { ShuttleGpsUpdateDto } from './shuttle.dto';
import type { ShuttleTrackingContext, ShuttleTrackingStop } from './shuttle.service';

export interface ShuttleEtaEvent {
  shuttleTripId: string;
  nextPickupOrder: number;
  etaMinutes: number;
  estimatedArrivalTime: string;
  distanceMeters: number;
  updatedAt: string;
}

interface ShuttleEtaState {
  order?: number;
  latitude?: number;
  longitude?: number;
  etaMinutes?: number;
  lastProviderCallAt?: string;
  googleFailureCount: number;
  cooldownUntil?: string;
}

interface ProviderCalculation {
  result: EtaProviderResult;
  googleFailureCount: number;
  cooldownUntil?: string;
}

const SHUTTLE_DEFAULT_SPEED_KMH = 30;
const SHUTTLE_MIN_REPORTED_SPEED_KMH = 3;
const RELEASE_LOCK_SCRIPT =
  "if redis.call('GET', KEYS[1]) == ARGV[1] then return redis.call('DEL', KEYS[1]) else return 0 end";

@Injectable()
export class ShuttleEtaService {
  private readonly logger = pino({ name: ShuttleEtaService.name });

  constructor(
    private readonly redis: RedisService,
    private readonly googleProvider: GoogleRoutesEtaProvider,
    @Inject(ENV_TOKEN) private readonly env: Env,
  ) {}

  async handleGpsUpdate(
    gps: ShuttleGpsUpdateDto,
    context: ShuttleTrackingContext,
  ): Promise<ShuttleEtaEvent | undefined> {
    try {
      return await this.calculateEta(gps, context);
    } catch (error) {
      this.logger.warn(
        { err: error, shuttleTripId: gps.shuttleTripId },
        'Skipping Shuttle ETA calculation after provider/calculation failure',
      );
      return undefined;
    }
  }

  private async calculateEta(
    gps: ShuttleGpsUpdateDto,
    context: ShuttleTrackingContext,
  ): Promise<ShuttleEtaEvent | undefined> {
    const nextStop = this.findNextStop(context.stops);
    if (!nextStop) return undefined;

    const state = await this.readState(gps.shuttleTripId);
    const cached = await this.readCachedEta(gps.shuttleTripId, nextStop.pickupOrder);
    if (!this.shouldRecalculate(gps, nextStop, state, cached)) return undefined;

    const lockKey = shuttleEtaLockKey(gps.shuttleTripId, nextStop.pickupOrder);
    const owner = randomUUID();
    const acquired = await this.redis
      .getClient()
      .set(lockKey, owner, 'EX', SHUTTLE_ETA_LOCK_TTL_SECONDS, 'NX');
    if (acquired !== 'OK') return undefined;

    try {
      const calculation = await this.calculateWithProviders(gps, nextStop, state);
      const updatedAt = new Date().toISOString();
      const event: ShuttleEtaEvent = {
        shuttleTripId: gps.shuttleTripId,
        nextPickupOrder: nextStop.pickupOrder,
        etaMinutes: calculation.result.etaMinutes,
        estimatedArrivalTime: new Date(
          new Date(gps.recordedAt).getTime() +
            calculation.result.etaMinutes * SECONDS_PER_MINUTE * MILLISECONDS_PER_SECOND,
        ).toISOString(),
        distanceMeters: calculation.result.distanceMeters,
        updatedAt,
      };
      const nextState: ShuttleEtaState = {
        order: nextStop.pickupOrder,
        latitude: gps.latitude,
        longitude: gps.longitude,
        etaMinutes: event.etaMinutes,
        lastProviderCallAt: updatedAt,
        googleFailureCount: calculation.googleFailureCount,
        ...(calculation.cooldownUntil ? { cooldownUntil: calculation.cooldownUntil } : {}),
      };
      await this.redis
        .getClient()
        .multi()
        .set(
          shuttleEtaKey(gps.shuttleTripId, nextStop.pickupOrder),
          JSON.stringify(event),
          'EX',
          this.env.TRACKING_ETA_CACHE_TTL_SECONDS ?? SHUTTLE_ETA_TTL_SECONDS,
        )
        .set(
          shuttleEtaStateKey(gps.shuttleTripId),
          JSON.stringify(nextState),
          'EX',
          SHUTTLE_ETA_STATE_TTL_SECONDS,
        )
        .exec();
      return event;
    } finally {
      try {
        await this.redis.getClient().eval(RELEASE_LOCK_SCRIPT, 1, lockKey, owner);
      } catch (error) {
        this.logger.warn(
          { err: error, shuttleTripId: gps.shuttleTripId, pickupOrder: nextStop.pickupOrder },
          'Failed to release Shuttle ETA lock',
        );
      }
    }
  }

  private async calculateWithProviders(
    gps: ShuttleGpsUpdateDto,
    stop: ShuttleTrackingStop,
    state: ShuttleEtaState | null,
  ): Promise<ProviderCalculation> {
    const googleEnabled =
      this.env.GOOGLE_ROUTES_ENABLED === true && Boolean(this.env.GOOGLE_ROUTES_API_KEY);
    const cooldownActive = Boolean(
      state?.cooldownUntil && new Date(state.cooldownUntil).getTime() > Date.now(),
    );
    let googleFailureCount = state?.googleFailureCount ?? 0;
    let cooldownUntil = state?.cooldownUntil;

    if (googleEnabled && !cooldownActive) {
      const googleResult = await this.googleProvider.calculateCoordinates(
        { latitude: gps.latitude, longitude: gps.longitude },
        { latitude: stop.latitude, longitude: stop.longitude },
      );
      if (googleResult) return { result: googleResult, googleFailureCount: 0 };

      googleFailureCount += 1;
      if (googleFailureCount >= ETA_GOOGLE_FAILURE_THRESHOLD) {
        cooldownUntil = new Date(
          Date.now() +
            (this.env.TRACKING_ETA_FAILURE_COOLDOWN_SECONDS ?? 300) *
              MILLISECONDS_PER_SECOND,
        ).toISOString();
      }
    }

    return {
      result: this.calculateLocalEta(gps, stop),
      googleFailureCount,
      ...(cooldownUntil ? { cooldownUntil } : {}),
    };
  }

  private calculateLocalEta(
    gps: ShuttleGpsUpdateDto,
    stop: ShuttleTrackingStop,
  ): EtaProviderResult {
    const distanceMeters = Math.round(
      calculateDistanceMeters(gps.latitude, gps.longitude, stop.latitude, stop.longitude),
    );
    const speedKmh =
      gps.speedKmh && gps.speedKmh > SHUTTLE_MIN_REPORTED_SPEED_KMH
        ? gps.speedKmh
        : SHUTTLE_DEFAULT_SPEED_KMH;
    return {
      distanceMeters,
      etaMinutes: Math.max(
        1,
        Math.ceil(
          (distanceMeters / METERS_PER_KILOMETER / speedKmh) * SECONDS_PER_MINUTE,
        ),
      ),
    };
  }

  private findNextStop(stops: ShuttleTrackingStop[]): ShuttleTrackingStop | undefined {
    return [...stops]
      .sort((left, right) => left.pickupOrder - right.pickupOrder)
      .find((stop) => stop.status !== 'CANCELLED');
  }

  private async readState(shuttleTripId: string): Promise<ShuttleEtaState | null> {
    const payload = await this.redis.getClient().get(shuttleEtaStateKey(shuttleTripId));
    if (!payload) return null;
    try {
      const state = JSON.parse(payload) as Partial<ShuttleEtaState>;
      return {
        ...state,
        googleFailureCount:
          typeof state.googleFailureCount === 'number' ? state.googleFailureCount : 0,
      };
    } catch {
      return null;
    }
  }

  private async readCachedEta(
    shuttleTripId: string,
    order: number,
  ): Promise<ShuttleEtaEvent | null> {
    const payload = await this.redis.getClient().get(shuttleEtaKey(shuttleTripId, order));
    if (!payload) return null;
    try {
      const event = JSON.parse(payload) as ShuttleEtaEvent;
      return event.shuttleTripId === shuttleTripId &&
        event.nextPickupOrder === order &&
        Number.isFinite(event.etaMinutes)
        ? event
        : null;
    } catch {
      return null;
    }
  }

  private shouldRecalculate(
    gps: ShuttleGpsUpdateDto,
    stop: ShuttleTrackingStop,
    state: ShuttleEtaState | null,
    cached: ShuttleEtaEvent | null,
  ): boolean {
    if (!state?.lastProviderCallAt || state.order !== stop.pickupOrder) return true;
    const minimumIntervalSeconds = this.env.TRACKING_ETA_MIN_INTERVAL_SECONDS ?? 60;
    if (
      Date.now() - new Date(state.lastProviderCallAt).getTime() <
      minimumIntervalSeconds * MILLISECONDS_PER_SECOND
    ) {
      return false;
    }
    if (!cached) return true;
    if (cached.etaMinutes < ETA_RECALCULATE_SOON_THRESHOLD_MINUTES) return true;
    if (state.latitude === undefined || state.longitude === undefined) return true;
    return (
      calculateDistanceMeters(gps.latitude, gps.longitude, state.latitude, state.longitude) >
      ETA_RECALCULATE_DISTANCE_THRESHOLD_METERS
    );
  }
}
