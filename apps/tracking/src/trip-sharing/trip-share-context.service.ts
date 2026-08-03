import { Inject, Injectable, ServiceUnavailableException } from '@nestjs/common';
import type { TripStopSnapshot } from '../eta/trip-data.provider';
import { ROUTE_GEOMETRY_PROVIDER } from '../off-route/off-route.constants';
import type {
  DetailedRouteGeometryProvider,
  RouteGeometrySnapshot,
} from '../off-route/route-geometry.provider';
import { sanitizeRouteGeometryPoints } from '../tracking-data/route-geometry-sanitizer';
import type { TripShareAccessContext } from './trip-share-access.service';
import type {
  TripShareContextDto,
  TripSharePublicEtaDto,
  TripSharePublicGeometryDto,
  TripSharePublicLocationDto,
} from './trip-share-context.dto';
import { TripShareRouteStopsProvider } from './trip-share-route-stops.provider';
import {
  TripShareTrackingStateRepository,
  type TripShareLatestState,
} from './trip-share-tracking-state.repository';

const TERMINAL_STOP_STATUSES = new Set(['COMPLETED', 'ARRIVED', 'SKIPPED']);
const SECONDS_PER_MINUTE = 60;

@Injectable()
export class TripShareContextService {
  constructor(
    @Inject(ROUTE_GEOMETRY_PROVIDER)
    private readonly routes: DetailedRouteGeometryProvider,
    private readonly routeStops: TripShareRouteStopsProvider,
    private readonly state: TripShareTrackingStateRepository,
  ) {}

  async getContext(access: TripShareAccessContext): Promise<TripShareContextDto> {
    try {
      const [routeResult, stops, latest] = await Promise.all([
        this.routes.getDetailedRouteGeometry(access.tripId),
        this.routeStops.getRouteStops(access.tripId),
        this.state.findLatest(access.tripId),
      ]);
      if (routeResult.kind !== 'ok') this.unavailable();
      if (!Array.isArray(stops)) this.unavailable();

      const route = this.requireDetailedRoute(routeResult.snapshot);
      const nextStop = this.findNextStop(stops);
      const etaState = nextStop
        ? await this.state.findEta(access.tripId, nextStop.stopId)
        : null;
      const location = this.mapLocation(latest);
      const eta = etaState
        ? {
            estimatedArrivalAt: etaState.estimatedArrivalTime,
            remainingSeconds: etaState.etaMinutes * SECONDS_PER_MINUTE,
            delayMinutes: null,
            updatedAt: etaState.updatedAt,
          } satisfies TripSharePublicEtaDto
        : null;

      return {
        status: 'IN_PROGRESS',
        expiresAt: access.expiresAt.toISOString(),
        lastUpdatedAt: this.latestTimestamp(location?.recordedAt, eta?.updatedAt),
        vehicle: { location },
        route: {
          originName: route.originStation.name,
          destinationName: route.destinationStation.name,
          geometry: this.mapGeometry(route),
        },
        eta,
      };
    } catch (error) {
      if (error instanceof ServiceUnavailableException) throw error;
      this.unavailable();
    }
  }

  private requireDetailedRoute(snapshot: RouteGeometrySnapshot): Required<Pick<
  RouteGeometrySnapshot,
  'geometrySource' | 'originStation' | 'intermediateStops' | 'destinationStation'
  >> & RouteGeometrySnapshot & {
    originStation: NonNullable<RouteGeometrySnapshot['originStation']>;
    destinationStation: NonNullable<RouteGeometrySnapshot['destinationStation']>;
  } {
    if (!snapshot.geometrySource
      || !snapshot.originStation
      || !Array.isArray(snapshot.intermediateStops)
      || !snapshot.destinationStation) {
      this.unavailable();
    }
    return snapshot as Required<Pick<
    RouteGeometrySnapshot,
    'geometrySource' | 'originStation' | 'intermediateStops' | 'destinationStation'
    >> & RouteGeometrySnapshot & {
      originStation: NonNullable<RouteGeometrySnapshot['originStation']>;
      destinationStation: NonNullable<RouteGeometrySnapshot['destinationStation']>;
    };
  }

  private mapGeometry(snapshot: RouteGeometrySnapshot): TripSharePublicGeometryDto | null {
    if (snapshot.geometrySource !== 'ROUTE_POLYLINE') return null;
    const points = sanitizeRouteGeometryPoints(snapshot.points);
    if (points.length < 2) return null;
    return {
      type: 'LineString',
      coordinates: points.map((point) => [point.longitude, point.latitude]),
    };
  }

  private findNextStop(stops: TripStopSnapshot[]): TripStopSnapshot | null {
    return [...stops]
      .filter((stop) => !TERMINAL_STOP_STATUSES.has(stop.status?.toUpperCase() ?? ''))
      .sort((left, right) => left.sequence - right.sequence)[0] ?? null;
  }

  private mapLocation(latest: TripShareLatestState | null): TripSharePublicLocationDto | null {
    if (!latest) return null;
    return {
      latitude: latest.latitude,
      longitude: latest.longitude,
      heading: latest.headingDeg ?? null,
      speedKph: latest.speedKmh ?? null,
      recordedAt: latest.recordedAt,
    };
  }

  private latestTimestamp(left: string | undefined, right: string | undefined): string | null {
    if (!left) return right ?? null;
    if (!right) return left;
    return Date.parse(left) >= Date.parse(right) ? left : right;
  }

  private unavailable(): never {
    throw new ServiceUnavailableException({
      errorCode: 'TRACKING_TRIP_UNAVAILABLE',
      detail: 'Trip sharing context dependency is unavailable',
    });
  }
}
