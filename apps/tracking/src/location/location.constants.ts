export const TRACKING_SOCKET_PATH = '/tracking/socket.io';
export const TRACKING_LATEST_TTL_SECONDS = 300;
export const TRACKING_ACTIVE_TRIPS_KEY = 'tracking:active_trips';

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

export function trackingEtaKey(tripId: string, stopId: string): string {
  return `tracking:eta:${tripId}:${stopId}`;
}

export function trackingTripRoom(tripId: string): string {
  return `trip:${tripId}`;
}
