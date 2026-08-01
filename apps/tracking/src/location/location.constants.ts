export const TRACKING_SOCKET_PATH = '/tracking/socket.io';
export const TRACKING_LATEST_TTL_SECONDS = 300;
export const TRACKING_GPS_IDEMPOTENCY_TTL_SECONDS = 86_400;
export const TRACKING_ACTIVE_TRIPS_KEY = 'tracking:active_trips';
export const ROUTE_SNAP_THRESHOLD_METERS = 50;

export function trackingLatestKey(tripId: string): string {
  return `tracking:latest:${tripId}`;
}

export function trackingGpsBufferKey(tripId: string): string {
  return `tracking:gps_buffer:${tripId}`;
}

export function trackingGpsProcessingKey(tripId: string): string {
  return `tracking:gps_buffer:${tripId}:processing`;
}

export function trackingGpsIdleKey(tripId: string): string {
  return `tracking:gps_idle:${tripId}`;
}

export function trackingGpsIdempotencyKey(tripId: string, recordedAt: string): string {
  return `tracking:gps_idempotency:${tripId}:${recordedAt}`;
}

export function trackingEtaKey(tripId: string, stopId: string): string {
  return `tracking:eta:${tripId}:${stopId}`;
}

export function trackingTripRoom(tripId: string): string {
  return `trip:${tripId}`;
}
