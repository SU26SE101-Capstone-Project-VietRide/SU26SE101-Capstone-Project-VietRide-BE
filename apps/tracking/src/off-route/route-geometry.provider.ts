export interface RouteGeometryPoint {
  latitude: number;
  longitude: number;
}

export interface RouteGeometryStation {
  stationId: string;
  name: string;
  latitude: number;
  longitude: number;
}

export interface RouteGeometryIntermediateStop {
  stopId: string;
  name: string;
  sequence: number;
  latitude: number;
  longitude: number;
}

export type RouteGeometrySource = 'ROUTE_POLYLINE' | 'STOPS_ONLY';

export interface RouteGeometrySnapshot {
  tripId: string;
  effectiveRouteId?: string;
  points: RouteGeometryPoint[];
  alertRecipientUserIds?: string[];
  geometrySource?: RouteGeometrySource;
  originStation?: RouteGeometryStation | null;
  intermediateStops?: RouteGeometryIntermediateStop[];
  destinationStation?: RouteGeometryStation | null;
}

export type RouteGeometryFetchResult =
  | { kind: 'ok'; snapshot: RouteGeometrySnapshot }
  | { kind: 'not_found' }
  | { kind: 'unavailable' };

export interface RouteGeometryProvider {
  peekCachedRouteGeometry(tripId: string): RouteGeometrySnapshot | null;
  getRouteGeometry(tripId: string): Promise<RouteGeometrySnapshot | null>;
  invalidateRouteGeometry(tripId: string): void;
}

export interface DetailedRouteGeometryProvider extends RouteGeometryProvider {
  getDetailedRouteGeometry(
    tripId: string,
    options?: { bypassCache?: boolean },
  ): Promise<RouteGeometryFetchResult>;
}

export interface RouteProjection {
  point: RouteGeometryPoint;
  distanceMeters: number;
  progressMeters: number;
}

const METERS_PER_DEGREE_LATITUDE = 111_320;

export function projectPointToRoute(
  point: RouteGeometryPoint,
  routePoints: RouteGeometryPoint[],
): RouteProjection | null {
  if (routePoints.length < 2) return null;
  let best: RouteProjection | null = null;
  let progressMeters = 0;
  for (let index = 0; index < routePoints.length - 1; index += 1) {
    const start = routePoints[index];
    const end = routePoints[index + 1];
    if (!start || !end) continue;
    const local = projectToLocalMeters(point, start, end);
    const segmentLengthSquared = local.segmentX ** 2 + local.segmentY ** 2;
    const rawPosition = segmentLengthSquared === 0
      ? 0
      : (local.pointX * local.segmentX + local.pointY * local.segmentY) / segmentLengthSquared;
    const position = Math.max(0, Math.min(1, rawPosition));
    const nearestX = local.segmentX * position;
    const nearestY = local.segmentY * position;
    const segmentLengthMeters = Math.sqrt(segmentLengthSquared);
    const candidate: RouteProjection = {
      point: {
        latitude: start.latitude + (end.latitude - start.latitude) * position,
        longitude: start.longitude + (end.longitude - start.longitude) * position,
      },
      distanceMeters: Math.hypot(local.pointX - nearestX, local.pointY - nearestY),
      progressMeters: progressMeters + segmentLengthMeters * position,
    };
    if (!best || candidate.distanceMeters < best.distanceMeters) best = candidate;
    progressMeters += segmentLengthMeters;
  }
  return best;
}

function projectToLocalMeters(
  point: RouteGeometryPoint,
  segmentStart: RouteGeometryPoint,
  segmentEnd: RouteGeometryPoint,
): { pointX: number; pointY: number; segmentX: number; segmentY: number } {
  const referenceLatitudeRadians = (segmentStart.latitude * Math.PI) / 180;
  const metersPerDegreeLongitude = METERS_PER_DEGREE_LATITUDE * Math.cos(referenceLatitudeRadians);
  return {
    pointX: (point.longitude - segmentStart.longitude) * metersPerDegreeLongitude,
    pointY: (point.latitude - segmentStart.latitude) * METERS_PER_DEGREE_LATITUDE,
    segmentX: (segmentEnd.longitude - segmentStart.longitude) * metersPerDegreeLongitude,
    segmentY: (segmentEnd.latitude - segmentStart.latitude) * METERS_PER_DEGREE_LATITUDE,
  };
}
