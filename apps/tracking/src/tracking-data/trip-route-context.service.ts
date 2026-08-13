import { Inject, Injectable, NotFoundException, ServiceUnavailableException } from '@nestjs/common';
import { createHash } from 'node:crypto';
import { ROUTE_GEOMETRY_PROVIDER } from '../off-route/off-route.constants';
import type {
  DetailedRouteGeometryProvider,
  RouteGeometryIntermediateStop,
  RouteGeometryStation,
} from '../off-route/route-geometry.provider';
import { isValidRouteCoordinate, sanitizeRouteGeometryPoints } from './route-geometry-sanitizer';

export interface PublicRoutePointDto {
  latitude: number;
  longitude: number;
}

export interface PublicRouteStationDto extends PublicRoutePointDto {
  stationId: string;
  name: string;
}

export interface PublicRouteIntermediateStopDto extends PublicRoutePointDto {
  stopId: string;
  name: string;
  sequence: number;
}

export interface PublicTripRouteContextDto {
  tripId: string;
  tripStatus?: string;
  geometry: {
    source: 'ROUTE_POLYLINE';
    points: PublicRoutePointDto[];
  } | null;
  originStation: PublicRouteStationDto | null;
  intermediateStops: PublicRouteIntermediateStopDto[];
  destinationStation: PublicRouteStationDto | null;
}

export interface PublicTripRouteContextResult {
  data: PublicTripRouteContextDto;
  etag: string;
}

@Injectable()
export class TripRouteContextService {
  constructor(
    @Inject(ROUTE_GEOMETRY_PROVIDER)
    private readonly routeGeometryProvider: DetailedRouteGeometryProvider,
  ) {}

  async getRouteContext(tripId: string): Promise<PublicTripRouteContextResult> {
    const result = await this.routeGeometryProvider.getDetailedRouteGeometry(tripId, { bypassCache: true });
    if (result.kind === 'not_found') {
      throw new NotFoundException({
        errorCode: 'TRIP_NOT_FOUND',
        detail: `Trip ${tripId} not found`,
      });
    }
    if (result.kind === 'unavailable') {
      throw this.routeContextUnavailable();
    }

    const snapshot = result.snapshot;
    if (!snapshot.geometrySource
      || snapshot.originStation === undefined
      || snapshot.intermediateStops === undefined
      || snapshot.destinationStation === undefined) {
      throw this.routeContextUnavailable();
    }

    const sanitizedPoints = sanitizeRouteGeometryPoints(snapshot.points);
    const geometry = snapshot.geometrySource === 'ROUTE_POLYLINE' && sanitizedPoints.length >= 2
      ? {
          source: 'ROUTE_POLYLINE' as const,
          points: sanitizedPoints,
        }
      : null;
    const data: PublicTripRouteContextDto = {
      tripId: snapshot.tripId,
      ...(snapshot.tripStatus ? { tripStatus: snapshot.tripStatus } : {}),
      geometry,
      originStation: mapStation(snapshot.originStation),
      intermediateStops: snapshot.intermediateStops
        .map(mapIntermediateStop)
        .filter((stop): stop is PublicRouteIntermediateStopDto => stop !== null),
      destinationStation: mapStation(snapshot.destinationStation),
    };

    const etagHash = createHash('sha256');
    if (snapshot.effectiveRouteId) etagHash.update(snapshot.effectiveRouteId);
    etagHash.update(JSON.stringify(data));
    return {
      data,
      etag: `"${etagHash.digest('hex')}"`,
    };
  }

  private routeContextUnavailable(): ServiceUnavailableException {
    return new ServiceUnavailableException({
      errorCode: 'TRACKING_ROUTE_CONTEXT_UNAVAILABLE',
      detail: 'Trip route context provider is unavailable',
    });
  }
}

function mapStation(station: RouteGeometryStation | null): PublicRouteStationDto | null {
  if (!station || !isValidRouteCoordinate(station)) return null;
  return {
    stationId: station.stationId,
    name: station.name,
    latitude: station.latitude,
    longitude: station.longitude,
  };
}

function mapIntermediateStop(
  stop: RouteGeometryIntermediateStop,
): PublicRouteIntermediateStopDto | null {
  if (!isValidRouteCoordinate(stop)) return null;
  return {
    stopId: stop.stopId,
    name: stop.name,
    sequence: stop.sequence,
    latitude: stop.latitude,
    longitude: stop.longitude,
  };
}
