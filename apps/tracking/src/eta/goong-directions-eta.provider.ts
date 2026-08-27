import { Inject, Injectable } from '@nestjs/common';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import type { GpsUpdateEvent } from '../location/location.service';
import type { EtaBatchTargetResult, EtaProvider, EtaProviderResult } from './eta-provider';
import type { TripStopSnapshot } from './trip-data.provider';
import { SECONDS_PER_MINUTE } from './eta.constants';

export interface RouteCoordinate {
  latitude: number;
  longitude: number;
}

interface GoongLeg {
  distanceMeters: number;
  durationSeconds: number;
}

const DEFAULT_MAX_DESTINATIONS_PER_REQUEST = 10;
const MAX_SNAP_DELTA_DEGREES = 0.02;

@Injectable()
export class GoongDirectionsEtaProvider implements EtaProvider {
  constructor(@Inject(ENV_TOKEN) private readonly env: Env) {}

  async calculate(gps: GpsUpdateEvent, stop: TripStopSnapshot): Promise<EtaProviderResult | null> {
    return this.calculateCoordinates(gps, stop);
  }

  async calculateCoordinates(
    origin: RouteCoordinate,
    destination: RouteCoordinate,
  ): Promise<EtaProviderResult | null> {
    const legs = await this.computeLegs(origin, [destination]);
    const leg = legs?.[0];
    if (!leg || legs?.length !== 1) return null;
    return {
      distanceMeters: Math.round(leg.distanceMeters),
      etaMinutes: Math.max(1, Math.ceil(leg.durationSeconds / SECONDS_PER_MINUTE)),
    };
  }

  async calculateBatch(
    gps: GpsUpdateEvent,
    targets: TripStopSnapshot[],
  ): Promise<EtaBatchTargetResult[] | null> {
    if (targets.length === 0) return [];
    const results: EtaBatchTargetResult[] = [];
    const maxDestinations =
      this.env.GOONG_MAX_DESTINATIONS_PER_REQUEST ?? DEFAULT_MAX_DESTINATIONS_PER_REQUEST;
    const dwellMinutes = this.env.TRIP_STOP_DWELL_MINUTES ?? 20;
    let origin: RouteCoordinate = gps;
    let cumulativeDistanceMeters = 0;
    let cumulativeDurationSeconds = 0;
    let targetOffset = 0;

    while (targetOffset < targets.length) {
      const chunk = targets.slice(targetOffset, targetOffset + maxDestinations);
      const legs = await this.computeLegs(origin, chunk);
      if (!legs || legs.length !== chunk.length) return null;

      for (let index = 0; index < chunk.length; index += 1) {
        const target = chunk[index];
        const leg = legs[index];
        if (!target || !leg) return null;
        cumulativeDistanceMeters += leg.distanceMeters;
        cumulativeDurationSeconds += leg.durationSeconds;
        results.push({
          targetId: target.stopId,
          distanceMeters: Math.round(cumulativeDistanceMeters),
          etaMinutes: Math.max(
            1,
            Math.ceil(cumulativeDurationSeconds / SECONDS_PER_MINUTE) +
              dwellMinutes * (targetOffset + index),
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
    targets: RouteCoordinate[],
  ): Promise<GoongLeg[] | null> {
    if (targets.length === 0) return [];
    const baseUrl = this.env.GOONG_BASE_URL ?? 'https://rsapi.goong.io';
    const requestUrl = new URL('/v2/direction', baseUrl);
    requestUrl.searchParams.set('origin', this.formatCoordinate(origin));
    requestUrl.searchParams.set(
      'destination',
      targets.map((target) => this.formatCoordinate(target)).join(';'),
    );
    requestUrl.searchParams.set('vehicle', 'car');
    requestUrl.searchParams.set('alternatives', 'false');
    requestUrl.searchParams.set('api_key', this.env.GOONG_API_KEY ?? '');

    const controller = new AbortController();
    const timeout = setTimeout(
      () => controller.abort(),
      this.env.TRACKING_ROUTING_TIMEOUT_MS ?? 1_500,
    );
    try {
      const response = await fetch(requestUrl, { method: 'GET', signal: controller.signal });
      if (!response.ok) return null;
      const body: unknown = await response.json();
      return this.readLegs(body, origin, targets);
    } catch {
      return null;
    } finally {
      clearTimeout(timeout);
    }
  }

  private readLegs(
    body: unknown,
    origin: RouteCoordinate,
    targets: RouteCoordinate[],
  ): GoongLeg[] | null {
    if (!body || typeof body !== 'object') return null;
    const routes = (body as { routes?: unknown }).routes;
    if (!Array.isArray(routes) || routes.length === 0) return null;
    const firstRoute = routes[0] as unknown;
    if (!firstRoute || typeof firstRoute !== 'object') return null;
    const legs = (firstRoute as { legs?: unknown }).legs;
    if (!Array.isArray(legs) || legs.length !== targets.length) return null;

    const parsed: GoongLeg[] = [];
    const expectedStarts = [origin, ...targets.slice(0, -1)];
    for (let index = 0; index < legs.length; index += 1) {
      const leg = legs[index] as unknown;
      const expectedEnd = targets[index];
      const expectedStart = index === 0 ? origin : targets[index - 1];
      if (!leg || typeof leg !== 'object' || !expectedStart || !expectedEnd) return null;
      const distanceMeters = this.readMetricValue((leg as { distance?: unknown }).distance);
      const durationSeconds = this.readMetricValue((leg as { duration?: unknown }).duration);
      const start = this.readCoordinate((leg as { start_location?: unknown }).start_location);
      const end = this.readCoordinate((leg as { end_location?: unknown }).end_location);
      if (
        distanceMeters === null ||
        durationSeconds === null ||
        !start ||
        !end ||
        !this.coordinatesMatchInOrder(start, expectedStart, expectedStarts, index) ||
        !this.coordinatesMatchInOrder(end, expectedEnd, targets, index)
      ) {
        return null;
      }
      parsed.push({ distanceMeters, durationSeconds });
    }
    return parsed;
  }

  private readMetricValue(metric: unknown): number | null {
    if (!metric || typeof metric !== 'object') return null;
    const value = (metric as { value?: unknown }).value;
    return typeof value === 'number' && Number.isFinite(value) && value >= 0 ? value : null;
  }

  private readCoordinate(value: unknown): RouteCoordinate | null {
    if (!value || typeof value !== 'object') return null;
    const latitude = (value as { lat?: unknown }).lat;
    const longitude = (value as { lng?: unknown }).lng;
    return typeof latitude === 'number' &&
      Number.isFinite(latitude) &&
      typeof longitude === 'number' &&
      Number.isFinite(longitude)
      ? { latitude, longitude }
      : null;
  }

  private coordinatesMatchInOrder(
    actual: RouteCoordinate,
    expected: RouteCoordinate,
    orderedCandidates: RouteCoordinate[],
    expectedIndex: number,
  ): boolean {
    if (
      Math.abs(actual.latitude - expected.latitude) > MAX_SNAP_DELTA_DEGREES ||
      Math.abs(actual.longitude - expected.longitude) > MAX_SNAP_DELTA_DEGREES
    ) {
      return false;
    }
    const expectedDistance = this.coordinateDistanceSquared(actual, expected);
    return orderedCandidates.every(
      (candidate, index) =>
        index === expectedIndex ||
        this.sameCoordinate(candidate, expected) ||
        this.coordinateDistanceSquared(actual, candidate) >= expectedDistance,
    );
  }

  private coordinateDistanceSquared(left: RouteCoordinate, right: RouteCoordinate): number {
    return (left.latitude - right.latitude) ** 2 + (left.longitude - right.longitude) ** 2;
  }

  private sameCoordinate(left: RouteCoordinate, right: RouteCoordinate): boolean {
    return left.latitude === right.latitude && left.longitude === right.longitude;
  }

  private formatCoordinate(coordinate: RouteCoordinate): string {
    return `${coordinate.latitude},${coordinate.longitude}`;
  }
}
