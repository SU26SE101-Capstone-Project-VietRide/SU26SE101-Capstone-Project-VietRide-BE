import { Inject, Injectable } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import { randomUUID } from 'node:crypto';
import pino from 'pino';
import { TRIP_DATA_PROVIDER } from '../eta/eta.constants';
import type { EtaUpdateEvent } from '../eta/eta.service';
import type { TripDataProvider, TripStopSnapshot } from '../eta/trip-data.provider';
import { TRACKING_ACTIVE_TRIPS_KEY, trackingEtaKey } from '../location/location.constants';
import { TrackingPrismaService } from '../prisma/tracking-prisma.service';
import { RouteStateGenerationRegistry } from '../route-state/route-state-generation.registry';
import {
  MILLISECONDS_PER_MINUTE,
  TRIP_DELAY_DEDUPE_TTL_SECONDS,
  TRIP_DELAY_LOCK_TTL_SECONDS,
  TRIP_DELAY_STATE_TTL_SECONDS,
  TRIP_DELAY_THRESHOLD_MINUTES,
  TRIP_DELAY_WINDOW_MS,
  TRIP_DELAYED_EVENT_TYPE,
  trackingTripDelayedDedupeKey,
  trackingTripDelayLockKey,
  trackingTripDelayStateKey,
} from './trip-delay.constants';

export type TripDelayStatus = 'DELAYED' | 'ON_TIME' | 'UNKNOWN';
export type TripDelayTransition = 'DELAYED' | 'DELAY_CLEARED';

export interface TripDelayedPayload {
  tripId: string;
  stopId: string;
  alertRecipientUserIds?: string[];
  staticEstimatedArrivalTime: string;
  dynamicEstimatedArrivalTime: string;
  delayMinutes: number;
  detectedAt: string;
  dedupeKey: string;
}

export interface TripDelayEtaUpdate extends EtaUpdateEvent {
  delayed: boolean;
  delayStatus: TripDelayStatus;
  delayMinutes: number | null;
  statusTransition?: TripDelayTransition;
}

interface CachedEta {
  tripId: string;
  stopId: string;
  estimatedArrivalTime: string;
}

interface TripDelayState {
  tripId: string;
  stopId: string;
  stopSequence?: number;
  delayStatus: Exclude<TripDelayStatus, 'UNKNOWN'>;
  delayMinutes: number;
  evaluatedAt: string;
}

interface DelayEvaluation {
  state: TripDelayState;
  statusTransition?: TripDelayTransition;
  payload?: TripDelayedPayload;
  eventCreated: boolean;
}

type DelayEvaluationMode = 'background' | 'realtime';

const TERMINAL_STOP_STATUSES = new Set(['ARRIVED', 'SKIPPED']);
const RELEASE_DELAY_LOCK_SCRIPT =
  "if redis.call('GET', KEYS[1]) == ARGV[1] then return redis.call('DEL', KEYS[1]) else return 0 end";

@Injectable()
export class TripDelayService {
  private readonly logger = pino({ name: TripDelayService.name });

  constructor(
    private readonly redis: RedisService,
    private readonly prisma: TrackingPrismaService,
    @Inject(TRIP_DATA_PROVIDER) private readonly tripDataProvider: TripDataProvider,
    private readonly routeStateGeneration: RouteStateGenerationRegistry,
  ) {}

  async detectDelayedTrips(): Promise<number> {
    const tripIds = await this.redis.getClient().smembers(TRACKING_ACTIVE_TRIPS_KEY);
    let created = 0;

    for (const tripId of tripIds) {
      created += await this.detectTripDelay(tripId);
    }

    return created;
  }

  async detectTripDelay(tripId: string): Promise<number> {
    const routeGeneration = this.routeStateGeneration.capture(tripId);
    try {
      return await this.evaluateTrip(tripId, routeGeneration);
    } catch (error) {
      this.logger.warn({ err: error, tripId }, 'Skipping trip delayed detection');
      return 0;
    }
  }

  async handleEtaUpdate(eta: EtaUpdateEvent): Promise<TripDelayEtaUpdate> {
    const routeGeneration = this.routeStateGeneration.capture(eta.tripId);
    try {
      const stops = await this.tripDataProvider.getRouteStops(eta.tripId);
      if (!this.routeStateGeneration.isCurrent(eta.tripId, routeGeneration)) {
        return await this.buildUnknownUpdate(eta, false);
      }
      const stop = stops.find((candidate) => candidate.stopId === eta.stopId);
      if (!stop || TERMINAL_STOP_STATUSES.has(stop.status ?? '')) {
        return await this.buildUnknownUpdate(eta);
      }

      const cachedEta: CachedEta = {
        tripId: eta.tripId,
        stopId: eta.stopId,
        estimatedArrivalTime: eta.estimatedArrivalTime,
      };
      const evaluation = await this.withDelayStateLock(
        eta.tripId,
        () => this.evaluateStopEta(stop, cachedEta, 'realtime', routeGeneration),
      );
      if (!evaluation) {
        const includePrevious = this.routeStateGeneration.isCurrent(
          eta.tripId,
          routeGeneration,
        );
        return await this.buildUnknownUpdate(eta, includePrevious);
      }

      return {
        ...eta,
        delayed: evaluation.state.delayStatus === 'DELAYED',
        delayStatus: evaluation.state.delayStatus,
        delayMinutes: evaluation.state.delayMinutes,
        ...(evaluation.statusTransition ? { statusTransition: evaluation.statusTransition } : {}),
      };
    } catch (error) {
      this.logger.warn(
        { err: error, tripId: eta.tripId, stopId: eta.stopId },
        'Skipping realtime delayed evaluation',
      );
      return await this.buildUnknownUpdate(eta);
    }
  }

  private async evaluateTrip(tripId: string, routeGeneration: number): Promise<number> {
    const stops = await this.tripDataProvider.getRouteStops(tripId);
    if (!this.routeStateGeneration.isCurrent(tripId, routeGeneration)) return 0;
    let created = 0;

    for (const stop of stops) {
      if (!this.routeStateGeneration.isCurrent(tripId, routeGeneration)) return 0;
      if (TERMINAL_STOP_STATUSES.has(stop.status ?? '') || !stop.estimatedArrivalTime) continue;

      const eta = await this.readCachedEta(tripId, stop.stopId);
      if (!this.routeStateGeneration.isCurrent(tripId, routeGeneration)) return 0;
      if (!eta) continue;

      const evaluation = await this.withDelayStateLock(
        tripId,
        () => this.evaluateStopEta(stop, eta, 'background', routeGeneration),
      );
      if (evaluation?.eventCreated) created += 1;
    }

    return created;
  }

  private async readCachedEta(tripId: string, stopId: string): Promise<CachedEta | null> {
    const raw = await this.redis.getClient().get(trackingEtaKey(tripId, stopId));
    if (!raw) return null;

    try {
      const parsed = JSON.parse(raw) as Partial<CachedEta>;
      if (
        parsed.tripId !== tripId ||
        parsed.stopId !== stopId ||
        typeof parsed.estimatedArrivalTime !== 'string'
      ) {
        return null;
      }
      return parsed as CachedEta;
    } catch {
      return null;
    }
  }

  private async readRealtimeDelayState(
    tripId: string,
    requestedStopId: string,
  ): Promise<TripDelayState | null> {
    const pointer = await this.readDelayStateAtKey(trackingTripDelayStateKey(tripId), tripId);
    if (pointer) return pointer;

    return this.readDelayStateAtKey(
      trackingTripDelayStateKey(tripId, requestedStopId),
      tripId,
      requestedStopId,
    );
  }

  private async readDelayStateAtKey(
    key: string,
    tripId: string,
    expectedStopId?: string,
  ): Promise<TripDelayState | null> {
    const raw = await this.redis.getClient().get(key);
    if (!raw) return null;

    try {
      const parsed = JSON.parse(raw) as Partial<TripDelayState>;
      if (
        parsed.tripId === tripId &&
        typeof parsed.stopId === 'string' &&
        (expectedStopId === undefined || parsed.stopId === expectedStopId) &&
        (parsed.stopSequence === undefined || (
          typeof parsed.stopSequence === 'number' &&
          Number.isInteger(parsed.stopSequence) &&
          parsed.stopSequence >= 0
        )) &&
        (parsed.delayStatus === 'DELAYED' || parsed.delayStatus === 'ON_TIME') &&
        typeof parsed.delayMinutes === 'number' &&
        Number.isFinite(parsed.delayMinutes) &&
        parsed.delayMinutes >= 0 &&
        typeof parsed.evaluatedAt === 'string'
      ) {
        return parsed as TripDelayState;
      }
    } catch {
      return null;
    }
    return null;
  }

  private async evaluateStopEta(
    stop: TripStopSnapshot,
    eta: CachedEta,
    mode: DelayEvaluationMode,
    routeGeneration: number,
  ): Promise<DelayEvaluation | null> {
    if (!stop.estimatedArrivalTime) return null;

    const staticEtaMs = new Date(stop.estimatedArrivalTime).getTime();
    const dynamicEtaMs = new Date(eta.estimatedArrivalTime).getTime();
    if (!Number.isFinite(staticEtaMs) || !Number.isFinite(dynamicEtaMs)) return null;

    const delayMinutes = Math.max(
      0,
      Math.floor((dynamicEtaMs - staticEtaMs) / MILLISECONDS_PER_MINUTE),
    );
    const delayStatus: Exclude<TripDelayStatus, 'UNKNOWN'> =
      delayMinutes > TRIP_DELAY_THRESHOLD_MINUTES ? 'DELAYED' : 'ON_TIME';
    const evaluatedAt = new Date().toISOString();
    const previous = mode === 'realtime'
      ? await this.readRealtimeDelayState(eta.tripId, eta.stopId)
      : null;
    if (!this.routeStateGeneration.isCurrent(eta.tripId, routeGeneration)) return null;
    const state: TripDelayState = {
      tripId: eta.tripId,
      stopId: eta.stopId,
      stopSequence: stop.sequence,
      delayStatus,
      delayMinutes,
      evaluatedAt,
    };

    await this.redis
      .getClient()
      .set(
        trackingTripDelayStateKey(eta.tripId, eta.stopId),
        JSON.stringify(state),
        'EX',
        TRIP_DELAY_STATE_TTL_SECONDS,
      );
    if (!this.routeStateGeneration.isCurrent(eta.tripId, routeGeneration)) return null;

    const staleRealtimeState = mode === 'realtime' && previous?.stopSequence !== undefined
      && stop.sequence < previous.stopSequence;
    if (mode === 'realtime' && !staleRealtimeState) {
      await this.redis
        .getClient()
        .set(
          trackingTripDelayStateKey(eta.tripId),
          JSON.stringify(state),
          'EX',
          TRIP_DELAY_STATE_TTL_SECONDS,
        );
      if (!this.routeStateGeneration.isCurrent(eta.tripId, routeGeneration)) return null;
    }

    const statusTransition = mode === 'realtime' && !staleRealtimeState
      ? this.resolveTransition(previous, state)
      : undefined;
    if (delayStatus !== 'DELAYED') {
      return {
        state,
        ...(statusTransition ? { statusTransition } : {}),
        eventCreated: false,
      };
    }

    const windowId = this.resolveWindowId(new Date(evaluatedAt).getTime());
    const dedupeKey = `trip-delay:${eta.tripId}:${eta.stopId}:${windowId}`;
    const payload: TripDelayedPayload = {
      tripId: eta.tripId,
      stopId: eta.stopId,
      ...(stop.alertRecipientUserIds?.length
        ? { alertRecipientUserIds: stop.alertRecipientUserIds }
        : {}),
      staticEstimatedArrivalTime: stop.estimatedArrivalTime,
      dynamicEstimatedArrivalTime: eta.estimatedArrivalTime,
      delayMinutes,
      detectedAt: evaluatedAt,
      dedupeKey,
    };

    let eventCreated = false;
    let markerExists = false;
    if (!this.routeStateGeneration.isCurrent(eta.tripId, routeGeneration)) return null;
    try {
      markerExists = (await this.redis.getClient().get(
        trackingTripDelayedDedupeKey(eta.tripId, eta.stopId, windowId),
      )) === '1';
    } catch (error) {
      this.logger.warn(
        { err: error, tripId: eta.tripId, stopId: eta.stopId, dedupeKey },
        'Trip delayed Redis marker read failed; falling back to durable Outbox dedupe',
      );
    }
    if (!markerExists) {
      if (!this.routeStateGeneration.isCurrent(eta.tripId, routeGeneration)) return null;
      try {
        eventCreated = await this.createOutboxEvent(payload);
      } catch (error) {
        this.logger.warn(
          { err: error, tripId: eta.tripId, stopId: eta.stopId, dedupeKey },
          'Trip delayed Outbox insert failed; retaining delay state for retry',
        );
      }
    }
    if (!this.routeStateGeneration.isCurrent(eta.tripId, routeGeneration)) return null;
    if (eventCreated) {
      await this.redis
        .getClient()
        .set(
          trackingTripDelayedDedupeKey(eta.tripId, eta.stopId, windowId),
          '1',
          'EX',
          TRIP_DELAY_DEDUPE_TTL_SECONDS,
        );
    }
    return {
      state,
      ...(statusTransition ? { statusTransition } : {}),
      payload,
      eventCreated,
    };
  }

  private async withDelayStateLock<T>(
    tripId: string,
    action: () => Promise<T>,
  ): Promise<T | null> {
    const lockKey = trackingTripDelayLockKey(tripId);
    const owner = randomUUID();
    const acquired = await this.redis
      .getClient()
      .set(lockKey, owner, 'EX', TRIP_DELAY_LOCK_TTL_SECONDS, 'NX');
    if (acquired !== 'OK') return null;

    try {
      return await action();
    } finally {
      try {
        await this.redis.getClient().eval(RELEASE_DELAY_LOCK_SCRIPT, 1, lockKey, owner);
      } catch (error) {
        this.logger.warn(
          { err: error, tripId },
          'Failed to release trip delay state lock',
        );
      }
    }
  }

  private resolveTransition(
    previous: TripDelayState | null,
    current: TripDelayState,
  ): TripDelayTransition | undefined {
    if (current.delayStatus === 'DELAYED') {
      if (!previous || previous.delayStatus !== 'DELAYED' || previous.stopId !== current.stopId) {
        return 'DELAYED';
      }
      return undefined;
    }

    return previous?.delayStatus === 'DELAYED' && previous.stopId === current.stopId
      ? 'DELAY_CLEARED'
      : undefined;
  }

  private resolveWindowId(timestampMs: number): string {
    return String(Math.floor(timestampMs / TRIP_DELAY_WINDOW_MS));
  }

  private async createOutboxEvent(payload: TripDelayedPayload): Promise<boolean> {
    try {
      await this.prisma.outboxEvent.create({
        data: {
          eventType: TRIP_DELAYED_EVENT_TYPE,
          dedupeKey: payload.dedupeKey,
          payload: {
            tripId: payload.tripId,
            stopId: payload.stopId,
            ...(payload.alertRecipientUserIds?.length
              ? { userIds: payload.alertRecipientUserIds }
              : {}),
            staticEstimatedArrivalTime: payload.staticEstimatedArrivalTime,
            dynamicEstimatedArrivalTime: payload.dynamicEstimatedArrivalTime,
            etaNew: payload.dynamicEstimatedArrivalTime,
            delayMinutes: payload.delayMinutes,
            detectedAt: payload.detectedAt,
          },
        },
      });
      return true;
    } catch (error) {
      if (this.isUniqueConstraintViolation(error)) return false;
      throw error;
    }
  }

  private async buildUnknownUpdate(
    eta: EtaUpdateEvent,
    includePrevious = true,
  ): Promise<TripDelayEtaUpdate> {
    const previous = includePrevious
      ? await this.readRealtimeDelayState(eta.tripId, eta.stopId).catch(() => null)
      : null;
    return {
      ...eta,
      delayed: previous?.delayStatus === 'DELAYED',
      delayStatus: 'UNKNOWN',
      delayMinutes: previous?.delayMinutes ?? null,
    };
  }

  private isUniqueConstraintViolation(error: unknown): boolean {
    return (
      typeof error === 'object' &&
      error !== null &&
      'code' in error &&
      error.code === 'P2002'
    );
  }
}
