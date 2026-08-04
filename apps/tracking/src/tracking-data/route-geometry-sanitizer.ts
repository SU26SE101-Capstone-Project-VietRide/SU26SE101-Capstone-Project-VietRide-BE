import type { RouteGeometryPoint } from '../off-route/route-geometry.provider';

const MAXIMUM_PUBLIC_GEOMETRY_POINTS = 1_000;

export interface SanitizedRouteGeometryPoint {
  latitude: number;
  longitude: number;
}

export function sanitizeRouteGeometryPoints(
  points: RouteGeometryPoint[],
  maximumPoints: number = MAXIMUM_PUBLIC_GEOMETRY_POINTS,
): SanitizedRouteGeometryPoint[] {
  const sanitized: SanitizedRouteGeometryPoint[] = [];
  for (const point of points) {
    if (!isValidRouteCoordinate(point)) continue;
    const previous = sanitized.at(-1);
    if (previous?.latitude === point.latitude && previous.longitude === point.longitude) continue;
    sanitized.push({ latitude: point.latitude, longitude: point.longitude });
  }
  return simplifyDouglasPeucker(sanitized, maximumPoints);
}

export function isValidRouteCoordinate(point: RouteGeometryPoint): boolean {
  return Number.isFinite(point.latitude)
    && Number.isFinite(point.longitude)
    && point.latitude >= -90
    && point.latitude <= 90
    && point.longitude >= -180
    && point.longitude <= 180;
}

function simplifyDouglasPeucker(
  points: SanitizedRouteGeometryPoint[],
  maximumPoints: number,
): SanitizedRouteGeometryPoint[] {
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
    .map((index) => points[index] as SanitizedRouteGeometryPoint);
}

interface SegmentCandidate {
  startIndex: number;
  endIndex: number;
  index: number;
  distanceSquared: number;
}

function findSegmentCandidate(
  points: SanitizedRouteGeometryPoint[],
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
  point: SanitizedRouteGeometryPoint,
  start: SanitizedRouteGeometryPoint,
  end: SanitizedRouteGeometryPoint,
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
