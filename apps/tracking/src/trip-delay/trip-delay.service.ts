import { Inject, Injectable } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import pino from 'pino';
import { TRIP_DATA_PROVIDER } from '../eta/eta.constants';
import type { EtaUpdateEvent } from '../eta/eta.service';
import type { TripDataProvider, TripStopSnapshot } from '../eta/trip-data.provider';
import { TRACKING_ACTIVE_TRIPS_KEY, trackingEtaKey } from '../location/location.constants';
import { TrackingPrismaService } from '../prisma/tracking-prisma.service';
import {
  MILLISECONDS_PER_MINUTE,
  TRIP_DELAY_DEDUPE_TTL_SECONDS,
  TRIP_DELAY_THRESHOLD_MINUTES,
  TRIP_DELAY_WINDOW_MS,
  TRIP_DELAYED_EVENT_TYPE,
  trackingTripDelayedDedupeKey,
} from './trip-delay.constants';

export interface TripDelayedPayload {
  tripId: string;
  stopId: string;
  staticEstimatedArrivalTime: string;
  dynamicEstimatedArrivalTime: string;
  delayMinutes: number;
  detectedAt: string;
}

export interface TripDelayEtaUpdate extends EtaUpdateEvent {
  delayed: boolean;
  delayMinutes?: number;
}

interface CachedEta {
  tripId: string;
  stopId: string;
  estimatedArrivalTime: string;
}

@Injectable()
export class TripDelayService {
  private readonly logger = pino({ name: TripDelayService.name });

  constructor(
    private readonly redis: RedisService,
    private readonly prisma: TrackingPrismaService,
    @Inject(TRIP_DATA_PROVIDER) private readonly tripDataProvider: TripDataProvider,
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
    try {
      return await this.evaluateTrip(tripId);
    } catch (error) {
      this.logger.warn({ err: error, tripId }, 'Skipping trip delayed detection');
      return 0;
    }
  }

  async handleEtaUpdate(eta: EtaUpdateEvent): Promise<TripDelayEtaUpdate> {
    try {
      const stops = await this.tripDataProvider.getRouteStops(eta.tripId);
      const stop = stops.find((candidate) => candidate.stopId === eta.stopId);
      const payload = await this.evaluateStopEta(stop, {
        tripId: eta.tripId,
        stopId: eta.stopId,
        estimatedArrivalTime: eta.estimatedArrivalTime,
      });

      if (!payload) {
        return { ...eta, delayed: false };
      }

      return { ...eta, delayed: true, delayMinutes: payload.delayMinutes };
    } catch (error) {
      this.logger.warn({ err: error, tripId: eta.tripId, stopId: eta.stopId }, 'Skipping realtime delayed evaluation');
      return { ...eta, delayed: false };
    }
  }

  private async evaluateTrip(tripId: string): Promise<number> {
    const stops = await this.tripDataProvider.getRouteStops(tripId);
    let created = 0;

    for (const stop of stops) {
      if (!stop.estimatedArrivalTime) continue;

      const eta = await this.readCachedEta(tripId, stop.stopId);
      if (!eta) continue;

      const payload = await this.evaluateStopEta(stop, eta);
      if (payload) created += 1;
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

  private async evaluateStopEta(stop: TripStopSnapshot | undefined, eta: CachedEta): Promise<TripDelayedPayload | null> {
    if (!stop?.estimatedArrivalTime) return null;

    const staticEtaMs = new Date(stop.estimatedArrivalTime).getTime();
    const dynamicEtaMs = new Date(eta.estimatedArrivalTime).getTime();
    if (!Number.isFinite(staticEtaMs) || !Number.isFinite(dynamicEtaMs)) return null;

    const delayMinutes = Math.floor((dynamicEtaMs - staticEtaMs) / MILLISECONDS_PER_MINUTE);
    if (delayMinutes <= TRIP_DELAY_THRESHOLD_MINUTES) return null;

    const windowId = this.resolveWindowId(dynamicEtaMs);
    const isFirstDetection = await this.markDelayPending(eta.tripId, eta.stopId, windowId);
    if (!isFirstDetection) return null;

    const payload: TripDelayedPayload = {
      tripId: eta.tripId,
      stopId: eta.stopId,
      staticEstimatedArrivalTime: stop.estimatedArrivalTime,
      dynamicEstimatedArrivalTime: eta.estimatedArrivalTime,
      delayMinutes,
      detectedAt: new Date().toISOString(),
    };
    await this.createOutboxEvent(payload);
    return payload;
  }

  private resolveWindowId(timestampMs: number): string {
    return String(Math.floor(timestampMs / TRIP_DELAY_WINDOW_MS));
  }

  private async markDelayPending(tripId: string, stopId: string, windowId: string): Promise<boolean> {
    const result = await this.redis
      .getClient()
      .set(
        trackingTripDelayedDedupeKey(tripId, stopId, windowId),
        '1',
        'EX',
        TRIP_DELAY_DEDUPE_TTL_SECONDS,
        'NX',
      );
    return result === 'OK';
  }

  private async createOutboxEvent(payload: TripDelayedPayload): Promise<void> {
    await this.prisma.outboxEvent.create({
      data: {
        eventType: TRIP_DELAYED_EVENT_TYPE,
        payload: {
          tripId: payload.tripId,
          stopId: payload.stopId,
          staticEstimatedArrivalTime: payload.staticEstimatedArrivalTime,
          dynamicEstimatedArrivalTime: payload.dynamicEstimatedArrivalTime,
          delayMinutes: payload.delayMinutes,
          detectedAt: payload.detectedAt,
        },
      },
    });
  }
}
