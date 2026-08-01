import { Inject, Injectable } from '@nestjs/common';
import { ROUTE_GEOMETRY_PROVIDER } from '../off-route/off-route.constants';
import { projectPointToRoute, type RouteGeometryProvider } from '../off-route/route-geometry.provider';
import { ETA_DEFAULT_SPEED_KMH, ETA_MIN_SPEED_KMH, METERS_PER_KILOMETER, SECONDS_PER_HOUR, SECONDS_PER_MINUTE } from './eta.constants';
import type { EtaProvider, EtaProviderResult } from './eta-provider';
import type { GpsUpdateEvent } from '../location/location.service';
import type { TripStopSnapshot } from './trip-data.provider';

@Injectable()
export class LocalRouteEtaProvider implements EtaProvider {
  constructor(@Inject(ROUTE_GEOMETRY_PROVIDER) private readonly routeGeometryProvider: RouteGeometryProvider) {}

  async calculate(gps: GpsUpdateEvent, stop: TripStopSnapshot): Promise<EtaProviderResult | null> {
    const route = this.routeGeometryProvider.peekCachedRouteGeometry(gps.tripId);
    if (!route) {
      void this.routeGeometryProvider.getRouteGeometry(gps.tripId).catch(() => null);
      return null;
    }
    if (route.points.length < 2) return null;
    const vehicle = projectPointToRoute(gps, route.points);
    const destination = projectPointToRoute(stop, route.points);
    if (!vehicle || !destination) return null;
    const remainingDistance = Math.max(0, destination.progressMeters - vehicle.progressMeters);
    const speedKmh = Math.max(gps.speedKmh ?? ETA_DEFAULT_SPEED_KMH, ETA_MIN_SPEED_KMH);
    const seconds = remainingDistance / ((speedKmh * METERS_PER_KILOMETER) / SECONDS_PER_HOUR);
    return {
      distanceMeters: Math.round(remainingDistance),
      etaMinutes: Math.max(1, Math.ceil(seconds / SECONDS_PER_MINUTE)),
    };
  }
}
