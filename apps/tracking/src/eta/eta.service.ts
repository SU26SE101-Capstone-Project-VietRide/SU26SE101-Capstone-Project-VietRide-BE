import { Inject, Injectable } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import pino from 'pino';
import { trackingEtaKey } from '../location/location.constants';
import type { GpsUpdateEvent } from '../location/location.service';
import {
  EARTH_RADIUS_METERS,
  ETA_CACHE_TTL_SECONDS,
  ETA_DEFAULT_SPEED_KMH,
  ETA_MIN_SPEED_KMH,
  ETA_RECALCULATE_DISTANCE_THRESHOLD_METERS,
  ETA_RECALCULATE_SOON_THRESHOLD_MINUTES,
  ETA_STOP_REACHED_DISTANCE_METERS,
  METERS_PER_KILOMETER,
  MILLISECONDS_PER_SECOND,
  SECONDS_PER_HOUR,
  SECONDS_PER_MINUTE,
  trackingEtaStateKey,
  TRIP_DATA_PROVIDER,
} from './eta.constants';
import type { TripDataProvider, TripStopSnapshot } from './trip-data.provider';

export interface EtaUpdateEvent {
  tripId: string;
  stopId: string;
  etaMinutes: number;
  estimatedArrivalTime: string;
  distanceMeters: number;
  updatedAt: string;
}

interface EtaState {
  latitude: number;
  longitude: number;
  etaMinutes: number;
  stopId: string;
}

@Injectable()
export class EtaService {
  private readonly logger = pino({ name: EtaService.name });

  constructor(
    private readonly redis: RedisService,
    @Inject(TRIP_DATA_PROVIDER) private readonly tripDataProvider: TripDataProvider,
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
    const nextStop = this.findNextStop(stops, gps);
    if (!nextStop) return null;

    const state = await this.readState(gps.tripId);
    if (!this.shouldRecalculate(gps, state)) return null;

    const distanceMeters = Math.round(calculateDistanceMeters(
      gps.latitude,
      gps.longitude,
      nextStop.latitude,
      nextStop.longitude,
    ));
    const etaMinutes = calculateEtaMinutes(distanceMeters, gps.speedKmh);
    const recordedAt = new Date(gps.recordedAt);
    const estimatedArrivalTime = new Date(
      recordedAt.getTime() + etaMinutes * SECONDS_PER_MINUTE * MILLISECONDS_PER_SECOND,
    ).toISOString();
    const updatedAt = new Date().toISOString();
    const event: EtaUpdateEvent = {
      tripId: gps.tripId,
      stopId: nextStop.stopId,
      etaMinutes,
      estimatedArrivalTime,
      distanceMeters,
      updatedAt,
    };

    await this.redis
      .getClient()
      .multi()
      .set(trackingEtaKey(gps.tripId, nextStop.stopId), JSON.stringify(event), 'EX', ETA_CACHE_TTL_SECONDS)
      .set(
        trackingEtaStateKey(gps.tripId),
        JSON.stringify({
          latitude: gps.latitude,
          longitude: gps.longitude,
          etaMinutes,
          stopId: nextStop.stopId,
        }),
        'EX',
        ETA_CACHE_TTL_SECONDS,
      )
      .exec();

    return event;
  }

  private findNextStop(stops: TripStopSnapshot[], gps: GpsUpdateEvent): TripStopSnapshot | null {
    const sortedStops = [...stops].sort((left, right) => left.sequence - right.sequence);
    return sortedStops.find((stop) => {
      const distanceMeters = calculateDistanceMeters(gps.latitude, gps.longitude, stop.latitude, stop.longitude);
      return distanceMeters > ETA_STOP_REACHED_DISTANCE_METERS;
    }) ?? null;
  }

  private async readState(tripId: string): Promise<EtaState | null> {
    const payload = await this.redis.getClient().get(trackingEtaStateKey(tripId));
    if (!payload) return null;

    try {
      const parsed = JSON.parse(payload) as Partial<EtaState>;
      if (
        typeof parsed.latitude !== 'number' ||
        typeof parsed.longitude !== 'number' ||
        typeof parsed.etaMinutes !== 'number' ||
        typeof parsed.stopId !== 'string'
      ) {
        return null;
      }
      return parsed as EtaState;
    } catch {
      return null;
    }
  }

  private shouldRecalculate(gps: GpsUpdateEvent, state: EtaState | null): boolean {
    if (!state) return true;
    if (state.etaMinutes < ETA_RECALCULATE_SOON_THRESHOLD_MINUTES) return true;

    const movedMeters = calculateDistanceMeters(gps.latitude, gps.longitude, state.latitude, state.longitude);
    return movedMeters > ETA_RECALCULATE_DISTANCE_THRESHOLD_METERS;
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
  const haversine =
    Math.sin(deltaLat / 2) * Math.sin(deltaLat / 2) +
    Math.cos(latARadians) * Math.cos(latBRadians) * Math.sin(deltaLon / 2) * Math.sin(deltaLon / 2);
  return EARTH_RADIUS_METERS * 2 * Math.atan2(Math.sqrt(haversine), Math.sqrt(1 - haversine));
}

function calculateEtaMinutes(distanceMeters: number, speedKmh?: number): number {
  const effectiveSpeedKmh = Math.max(speedKmh ?? ETA_DEFAULT_SPEED_KMH, ETA_MIN_SPEED_KMH);
  const seconds = distanceMeters / ((effectiveSpeedKmh * METERS_PER_KILOMETER) / SECONDS_PER_HOUR);
  return Math.max(1, Math.ceil(seconds / SECONDS_PER_MINUTE));
}

function degreesToRadians(value: number): number {
  return (value * Math.PI) / 180;
}
