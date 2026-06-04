export const OFF_ROUTE_EVENT_TYPE = 'OffRouteAlert';
export const OFF_ROUTE_DISTANCE_THRESHOLD_METERS = 500;
export const OFF_ROUTE_CONTINUOUS_THRESHOLD_MS = 120_000;
export const OFF_ROUTE_STATE_TTL_SECONDS = 86_400;
export const APPROXIMATE_METERS_PER_DEGREE_LATITUDE = 111_320;

export const ROUTE_GEOMETRY_PROVIDER = Symbol('ROUTE_GEOMETRY_PROVIDER');

export function trackingOffRouteSinceKey(tripId: string): string {
  return `tracking:off_route_since:${tripId}`;
}
