import { Inject, Injectable, ServiceUnavailableException } from '@nestjs/common';
import { randomUUID } from 'node:crypto';
import { z } from 'zod';
import { ENV_TOKEN } from '../app/tokens';
import {
  TrackingInternalJwtSigner,
  type TrackingInternalJwtClaims,
} from '../authorization/tracking-internal-jwt.signer';
import type { Env } from '../config/env.schema';
import type { TripStopSnapshot } from '../eta/trip-data.provider';

const INTERNAL_AUTH_HEADER = 'X-Internal-Auth';
const BEARER_PREFIX = 'Bearer ';
const ROUTE_STOP_SCHEMA = z.object({
  stopId: z.string(),
  latitude: z.number(),
  longitude: z.number(),
  sequence: z.number().int().min(0),
  status: z.string().nullish(),
  alertRecipientUserIds: z.array(z.string()).nullish(),
  estimatedArrivalTime: z.string().nullish(),
});
const ROUTE_STOPS_ENVELOPE_SCHEMA = z.object({
  success: z.literal(true),
  data: z.object({ stops: z.array(ROUTE_STOP_SCHEMA) }),
});

@Injectable()
export class TripShareRouteStopsProvider {
  constructor(
    @Inject(ENV_TOKEN) private readonly env: Env,
    private readonly signer: TrackingInternalJwtSigner,
  ) {}

  async getRouteStops(tripId: string): Promise<TripStopSnapshot[]> {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), this.env.TRACKING_DATA_PROVIDER_TIMEOUT_MS);

    try {
      const claims: TrackingInternalJwtClaims = {
        sub: 'tracking-service',
        reqId: randomUUID(),
      };
      const token = await this.signer.sign(claims);
      const response = await fetch(this.buildUrl(tripId), {
        method: 'GET',
        headers: {
          [INTERNAL_AUTH_HEADER]: `${BEARER_PREFIX}${token}`,
        },
        signal: controller.signal,
      });
      if (!response.ok) this.unavailable();

      const body = await response.json();
      const parsed = ROUTE_STOPS_ENVELOPE_SCHEMA.safeParse(body);
      if (!parsed.success) this.unavailable();

      return parsed.data.data.stops.map((stop) => ({
        stopId: stop.stopId,
        latitude: stop.latitude,
        longitude: stop.longitude,
        sequence: stop.sequence,
        ...(stop.status != null ? { status: stop.status } : {}),
        ...(stop.alertRecipientUserIds?.length
          ? { alertRecipientUserIds: stop.alertRecipientUserIds }
          : {}),
        ...(stop.estimatedArrivalTime != null
          ? { estimatedArrivalTime: stop.estimatedArrivalTime }
          : {}),
      }));
    } catch (error) {
      if (error instanceof ServiceUnavailableException) throw error;
      this.unavailable();
    } finally {
      clearTimeout(timeout);
    }
  }

  private buildUrl(tripId: string): string {
    const path = this.env.TRIP_ROUTE_STOPS_PATH.replace(':tripId', encodeURIComponent(tripId));
    return new URL(path, this.env.TRIP_SERVICE_BASE_URL).toString();
  }

  private unavailable(): never {
    throw new ServiceUnavailableException({
      errorCode: 'TRACKING_TRIP_UNAVAILABLE',
      detail: 'Trip sharing route stops provider is unavailable',
    });
  }
}
