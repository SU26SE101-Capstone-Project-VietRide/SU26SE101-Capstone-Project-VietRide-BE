export const ETA_CACHE_TTL_SECONDS = 60;
export const ETA_STOPS_ONLY_CACHE_TTL_SECONDS = 30;
export const ETA_STATE_TTL_SECONDS = 86_400;
export const ETA_RECALCULATE_DISTANCE_THRESHOLD_METERS = 500;
export const ETA_RECALCULATE_SOON_THRESHOLD_MINUTES = 15;
export const ETA_MIN_INTERVAL_SECONDS = 60;
export const ETA_DEFAULT_SPEED_KMH = 40;
export const ETA_MIN_SPEED_KMH = 5;
export const ETA_STOP_REACHED_DISTANCE_METERS = 50;
export const ETA_LOCK_TTL_SECONDS = 10;
export const ETA_FAILURE_COOLDOWN_SECONDS = 300;
export const ETA_PROVIDER_FAILURE_THRESHOLD = 3;
export const METERS_PER_KILOMETER = 1_000;
export const SECONDS_PER_HOUR = 3_600;
export const SECONDS_PER_MINUTE = 60;
export const MILLISECONDS_PER_SECOND = 1_000;
export const EARTH_RADIUS_METERS = 6_371_000;

export const TRIP_DATA_PROVIDER = Symbol('TRIP_DATA_PROVIDER');
export const GOONG_ETA_PROVIDER = Symbol('GOONG_ETA_PROVIDER');
export const LOCAL_ETA_PROVIDER = Symbol('LOCAL_ETA_PROVIDER');

export function trackingEtaStateKey(tripId: string): string {
  return `tracking:eta_state:${tripId}`;
}

export function trackingEtaBatchLockKey(tripId: string): string {
  return `tracking:eta_batch_lock:${tripId}`;
}
