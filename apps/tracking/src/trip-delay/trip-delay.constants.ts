export const TRIP_DELAYED_EVENT_TYPE = 'TripDelayed';
export const TRIP_DELAY_THRESHOLD_MINUTES = 30;
export const TRIP_DELAY_JOB_NAME = 'tracking-trip-delay-detect';
export const TRIP_DELAY_QUEUE_NAME = 'tracking-trip-delay';
export const TRIP_DELAY_SCHEDULER_ID = 'tracking-trip-delay-scheduler';
export const TRIP_DELAY_WORKER_CONCURRENCY = 1;
export const TRIP_DELAY_DEDUPE_TTL_SECONDS = 86_400;
export const TRIP_DELAY_WINDOW_MS = 300_000;
export const TRIP_DELAY_STATE_TTL_SECONDS = 86_400;
export const TRIP_DELAY_LOCK_TTL_SECONDS = 10;
export const TRIP_DELAY_STATE_KEY_PREFIX = 'tracking:trip_delay_state:';
export const TRIP_DELAY_LOCK_KEY_PREFIX = 'tracking:trip_delay_lock:';
export const MINUTES_PER_HOUR = 60;
export const MILLISECONDS_PER_MINUTE = 60_000;

export function trackingTripDelayedDedupeKey(tripId: string, stopId: string, windowId: string): string {
  return `tracking:trip_delayed:${tripId}:${stopId}:${windowId}`;
}

export function trackingTripDelayStateKey(tripId: string, stopId?: string): string {
  return stopId
    ? `${TRIP_DELAY_STATE_KEY_PREFIX}${tripId}:${stopId}`
    : `${TRIP_DELAY_STATE_KEY_PREFIX}${tripId}`;
}

export function trackingTripDelayLockKey(tripId: string): string {
  return `${TRIP_DELAY_LOCK_KEY_PREFIX}${tripId}`;
}
