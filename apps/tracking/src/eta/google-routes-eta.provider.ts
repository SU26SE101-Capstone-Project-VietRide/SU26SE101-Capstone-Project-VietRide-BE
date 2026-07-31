import { Inject, Injectable } from '@nestjs/common';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import type { GpsUpdateEvent } from '../location/location.service';
import type { EtaProvider, EtaProviderResult } from './eta-provider';
import type { TripStopSnapshot } from './trip-data.provider';
import { SECONDS_PER_MINUTE } from './eta.constants';

const GOOGLE_DURATION_PATTERN = /^(\d+(?:\.\d+)?)s$/;

@Injectable()
export class GoogleRoutesEtaProvider implements EtaProvider {
  constructor(@Inject(ENV_TOKEN) private readonly env: Env) {}

  async calculate(gps: GpsUpdateEvent, stop: TripStopSnapshot): Promise<EtaProviderResult | null> {
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
          origin: { location: { latLng: { latitude: gps.latitude, longitude: gps.longitude } } },
          destination: { location: { latLng: { latitude: stop.latitude, longitude: stop.longitude } } },
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
