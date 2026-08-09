import { Inject, Injectable } from '@nestjs/common';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import type { GpsUpdateEvent } from '../location/location.service';
import type { EtaBatchTargetResult, EtaProvider, EtaProviderResult } from './eta-provider';
import type { TripStopSnapshot } from './trip-data.provider';
import {
  ETA_MAX_TARGETS_PER_GOOGLE_REQUEST,
  SECONDS_PER_MINUTE,
} from './eta.constants';

const GOOGLE_DURATION_PATTERN = /^(\d+(?:\.\d+)?)s$/;

export interface RouteCoordinate {
  latitude: number;
  longitude: number;
}

@Injectable()
export class GoogleRoutesEtaProvider implements EtaProvider {
  constructor(@Inject(ENV_TOKEN) private readonly env: Env) {}

  async calculate(gps: GpsUpdateEvent, stop: TripStopSnapshot): Promise<EtaProviderResult | null> {
    return this.calculateCoordinates(gps, stop);
  }

  async calculateCoordinates(
    origin: RouteCoordinate,
    destination: RouteCoordinate,
  ): Promise<EtaProviderResult | null> {
    const baseUrl = this.env.GOOGLE_ROUTES_BASE_URL ?? 'https://routes.googleapis.com';
    const apiKey = this.env.GOOGLE_ROUTES_API_KEY ?? '';
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), this.env.TRACKING_GOOGLE_ROUTES_TIMEOUT_MS ?? 1_500);
    try {
      const response = await fetch(new URL('/directions/v2:computeRoutes', baseUrl), {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'X-Goog-Api-Key': apiKey,
          'X-Goog-FieldMask': 'routes.duration,routes.distanceMeters',
        },
        body: JSON.stringify({
          origin: { location: { latLng: { latitude: origin.latitude, longitude: origin.longitude } } },
          destination: {
            location: {
              latLng: { latitude: destination.latitude, longitude: destination.longitude },
            },
          },
          travelMode: 'DRIVE',
          routingPreference: 'TRAFFIC_AWARE',
          computeAlternativeRoutes: false,
          units: 'METRIC',
        }),
        signal: controller.signal,
      });
      if (!response.ok) return null;
      const body: unknown = await response.json();
      const route = this.readRoute(body);
      if (!route) return null;
      return {
        distanceMeters: Math.round(route.distanceMeters),
        etaMinutes: Math.max(1, Math.ceil(route.durationSeconds / SECONDS_PER_MINUTE)),
      };
    } catch {
      return null;
    } finally {
      clearTimeout(timeout);
    }
  }

  async calculateBatch(
    gps: GpsUpdateEvent,
    targets: TripStopSnapshot[],
  ): Promise<EtaBatchTargetResult[] | null> {
    if (targets.length === 0) return [];
    const results: EtaBatchTargetResult[] = [];
    let origin: RouteCoordinate = gps;
    let cumulativeDistanceMeters = 0;
    let cumulativeDurationSeconds = 0;
    let targetOffset = 0;
    const dwellMinutes = this.env.TRIP_STOP_DWELL_MINUTES ?? 20;

    while (targetOffset < targets.length) {
      const chunk = targets.slice(
        targetOffset,
        targetOffset + ETA_MAX_TARGETS_PER_GOOGLE_REQUEST,
      );
      const legs = await this.computeLegs(origin, chunk);
      if (!legs || legs.length !== chunk.length) return null;

      for (let index = 0; index < chunk.length; index += 1) {
        const target = chunk[index];
        const leg = legs[index];
        if (!target || !leg) return null;
        cumulativeDistanceMeters += leg.distanceMeters;
        cumulativeDurationSeconds += leg.durationSeconds;
        const globalIndex = targetOffset + index;
        results.push({
          targetId: target.stopId,
          distanceMeters: Math.round(cumulativeDistanceMeters),
          etaMinutes: Math.max(
            1,
            Math.ceil(cumulativeDurationSeconds / SECONDS_PER_MINUTE)
              + dwellMinutes * globalIndex,
          ),
        });
      }

      targetOffset += chunk.length;
      const boundary = chunk[chunk.length - 1];
      if (!boundary) return null;
      origin = boundary;
    }

    return results;
  }

  private async computeLegs(
    origin: RouteCoordinate,
    targets: TripStopSnapshot[],
  ): Promise<Array<{ distanceMeters: number; durationSeconds: number }> | null> {
    if (targets.length === 0) return [];
    const baseUrl = this.env.GOOGLE_ROUTES_BASE_URL ?? 'https://routes.googleapis.com';
    const apiKey = this.env.GOOGLE_ROUTES_API_KEY ?? '';
    const controller = new AbortController();
    const timeout = setTimeout(
      () => controller.abort(),
      this.env.TRACKING_GOOGLE_ROUTES_TIMEOUT_MS ?? 1_500,
    );
    try {
      const destination = targets[targets.length - 1];
      if (!destination) return null;
      const response = await fetch(new URL('/directions/v2:computeRoutes', baseUrl), {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'X-Goog-Api-Key': apiKey,
          'X-Goog-FieldMask': 'routes.legs.duration,routes.legs.distanceMeters',
        },
        body: JSON.stringify({
          origin: this.toWaypoint(origin),
          destination: this.toWaypoint(destination),
          intermediates: targets.slice(0, -1).map((target) => this.toWaypoint(target, true)),
          travelMode: 'DRIVE',
          routingPreference: 'TRAFFIC_AWARE',
          computeAlternativeRoutes: false,
          optimizeWaypointOrder: false,
          units: 'METRIC',
        }),
        signal: controller.signal,
      });
      if (!response.ok) return null;
      const body: unknown = await response.json();
      return this.readLegs(body);
    } catch {
      return null;
    } finally {
      clearTimeout(timeout);
    }
  }

  private toWaypoint(coordinate: RouteCoordinate, vehicleStopover = false): object {
    return {
      location: {
        latLng: {
          latitude: coordinate.latitude,
          longitude: coordinate.longitude,
        },
      },
      ...(vehicleStopover ? { vehicleStopover: true } : {}),
    };
  }

  private readLegs(
    body: unknown,
  ): Array<{ distanceMeters: number; durationSeconds: number }> | null {
    if (!body || typeof body !== 'object') return null;
    const routes = (body as { routes?: unknown }).routes;
    if (!Array.isArray(routes) || routes.length === 0) return null;
    const first = routes[0];
    if (!first || typeof first !== 'object') return null;
    const legs = (first as { legs?: unknown }).legs;
    if (!Array.isArray(legs)) return null;
    const parsed: Array<{ distanceMeters: number; durationSeconds: number }> = [];
    for (const leg of legs) {
      if (!leg || typeof leg !== 'object') return null;
      const distanceMeters = (leg as { distanceMeters?: unknown }).distanceMeters;
      const duration = (leg as { duration?: unknown }).duration;
      if (typeof distanceMeters !== 'number' || !Number.isFinite(distanceMeters) || distanceMeters < 0) return null;
      if (typeof duration !== 'string') return null;
      const match = GOOGLE_DURATION_PATTERN.exec(duration);
      if (!match) return null;
      const durationSeconds = Number(match[1]);
      if (!Number.isFinite(durationSeconds) || durationSeconds < 0) return null;
      parsed.push({ distanceMeters, durationSeconds });
    }
    return parsed;
  }

  private readRoute(body: unknown): { distanceMeters: number; durationSeconds: number } | null {
    if (!body || typeof body !== 'object') return null;
    const routes = (body as { routes?: unknown }).routes;
    if (!Array.isArray(routes) || routes.length === 0) return null;
    const first = routes[0];
    if (!first || typeof first !== 'object') return null;
    const distanceMeters = (first as { distanceMeters?: unknown }).distanceMeters;
    const duration = (first as { duration?: unknown }).duration;
    if (typeof distanceMeters !== 'number' || !Number.isFinite(distanceMeters) || distanceMeters < 0) return null;
    if (typeof duration !== 'string') return null;
    const match = GOOGLE_DURATION_PATTERN.exec(duration);
    if (!match) return null;
    const durationSeconds = Number(match[1]);
    return Number.isFinite(durationSeconds) && durationSeconds >= 0 ? { distanceMeters, durationSeconds } : null;
  }
}
