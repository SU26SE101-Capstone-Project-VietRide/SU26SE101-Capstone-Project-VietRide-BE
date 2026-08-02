import { Inject, Injectable, NotFoundException, ServiceUnavailableException } from '@nestjs/common';
import { createHash } from 'node:crypto';
import { ROUTE_GEOMETRY_PROVIDER } from '../off-route/off-route.constants';
import type {
  DetailedRouteGeometryProvider,
  RouteGeometryIntermediateStop,
  RouteGeometryPoint,
  RouteGeometryStation,
} from '../off-route/route-geometry.provider';

const MAXIMUM_PUBLIC_GEOMETRY_POINTS = 1_000;

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
    const result = await this.routeGeometryProvider.getDetailedRouteGeometry(tripId);
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

    const sanitizedPoints = sanitizePoints(snapshot.points);
    const geometry = snapshot.geometrySource === 'ROUTE_POLYLINE' && sanitizedPoints.length >= 2
      ? {
          source: 'ROUTE_POLYLINE' as const,
          points: simplifyDouglasPeucker(sanitizedPoints, MAXIMUM_PUBLIC_GEOMETRY_POINTS),
        }
      : null;
    const data: PublicTripRouteContextDto = {
      tripId: snapshot.tripId,
      geometry,
      originStation: mapStation(snapshot.originStation),
      intermediateStops: snapshot.intermediateStops
        .map(mapIntermediateStop)
        .filter((stop): stop is PublicRouteIntermediateStopDto => stop !== null),
      destinationStation: mapStation(snapshot.destinationStation),
    };

    return {
      data,
      etag: `"${createHash('sha256').update(JSON.stringify(data)).digest('hex')}"`,
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
  if (!station || !isValidCoordinate(station)) return null;
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
  if (!isValidCoordinate(stop)) return null;
  return {
    stopId: stop.stopId,
    name: stop.name,
    sequence: stop.sequence,
    latitude: stop.latitude,
    longitude: stop.longitude,
  };
}

function sanitizePoints(points: RouteGeometryPoint[]): PublicRoutePointDto[] {
  const sanitized: PublicRoutePointDto[] = [];
  for (const point of points) {
    if (!isValidCoordinate(point)) continue;
    const previous = sanitized.at(-1);
    if (previous?.latitude === point.latitude && previous.longitude === point.longitude) continue;
    sanitized.push({ latitude: point.latitude, longitude: point.longitude });
  }
  return sanitized;
}

function isValidCoordinate(point: RouteGeometryPoint): boolean {
  return Number.isFinite(point.latitude)
    && Number.isFinite(point.longitude)
    && point.latitude >= -90
    && point.latitude <= 90
    && point.longitude >= -180
    && point.longitude <= 180;
}

function simplifyDouglasPeucker(
  points: PublicRoutePointDto[],
  maximumPoints: number,
): PublicRoutePointDto[] {
  if (points.length <= maximumPoints) return points;

  const retained = new Set<number>([0, points.length - 1]);
  const segments: SegmentCandidate[] = [];
  const initial = findSegmentCandidate(points, 0, points.length - 1);
  if (initial) segments.push(initial);

  while (retained.size < maximumPoints && segments.length > 0) {
    segments.sort((left, right) =>
      right.distanceSquared - left.distanceSquared || left.index - right.index);
    const candidate = segments.shift();
    if (!candidate) break;
    retained.add(candidate.index);

    const left = findSegmentCandidate(points, candidate.startIndex, candidate.index);
    if (left) segments.push(left);
    const right = findSegmentCandidate(points, candidate.index, candidate.endIndex);
    if (right) segments.push(right);
  }

  return [...retained]
    .sort((left, right) => left - right)
    .map((index) => points[index] as PublicRoutePointDto);
}

interface SegmentCandidate {
  startIndex: number;
  endIndex: number;
  index: number;
  distanceSquared: number;
}

function findSegmentCandidate(
  points: PublicRoutePointDto[],
  startIndex: number,
  endIndex: number,
): SegmentCandidate | null {
  if (endIndex - startIndex <= 1) return null;
  const start = points[startIndex];
  const end = points[endIndex];
  if (!start || !end) return null;

  let bestIndex = -1;
  let bestDistanceSquared = -1;
  for (let index = startIndex + 1; index < endIndex; index += 1) {
    const point = points[index];
    if (!point) continue;
    const distanceSquared = perpendicularDistanceSquared(point, start, end);
    if (distanceSquared > bestDistanceSquared) {
      bestIndex = index;
      bestDistanceSquared = distanceSquared;
    }
  }

  return bestIndex < 0
    ? null
    : { startIndex, endIndex, index: bestIndex, distanceSquared: bestDistanceSquared };
}

function perpendicularDistanceSquared(
  point: PublicRoutePointDto,
  start: PublicRoutePointDto,
  end: PublicRoutePointDto,
): number {
  const segmentLatitude = end.latitude - start.latitude;
  const segmentLongitude = end.longitude - start.longitude;
  const segmentLengthSquared = segmentLatitude ** 2 + segmentLongitude ** 2;
  if (segmentLengthSquared === 0) {
    return (point.latitude - start.latitude) ** 2 + (point.longitude - start.longitude) ** 2;
  }

  const projection = Math.max(0, Math.min(1,
    ((point.latitude - start.latitude) * segmentLatitude
      + (point.longitude - start.longitude) * segmentLongitude) / segmentLengthSquared));
  const projectedLatitude = start.latitude + projection * segmentLatitude;
  const projectedLongitude = start.longitude + projection * segmentLongitude;
  return (point.latitude - projectedLatitude) ** 2 + (point.longitude - projectedLongitude) ** 2;
}
