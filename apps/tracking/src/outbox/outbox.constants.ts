export const OUTBOX_QUEUE_NAME = 'tracking-outbox-publish';
export const OUTBOX_JOB_NAME = 'tracking-outbox-publish';
export const OUTBOX_SCHEDULER_ID = 'tracking-outbox-publish-scheduler';
export const OUTBOX_WORKER_CONCURRENCY = 1;
export const OUTBOX_LAST_ERROR_MAX_LENGTH = 2_000;
// An event enters the durable DLQ after the sixth failed publish (retry_count > 5).
export const OUTBOX_MAX_RETRIES = 5;
export const OUTBOX_RETRY_BASE_DELAY_MS = 2_000;
export const OUTBOX_RETRY_MAX_DELAY_MS = 3_600_000;
export const OUTBOX_PUBLISHING_STALE_MS = 120_000;
export const OUTBOX_STALE_PUBLISHING_ERROR = 'Recovered from stale PUBLISHING';

export const TRIP_DELAYED_EVENT_TYPE = 'TripDelayed';
export const OFF_ROUTE_ALERT_EVENT_TYPE = 'OffRouteAlert';
export const APPROACHING_ALERT_EVENT_TYPE = 'ApproachingAlert';

export const TRACKING_TRIP_DELAYED_ROUTING_KEY = 'trip.trip.delayed';
export const TRACKING_OFF_ROUTE_ROUTING_KEY = 'tracking.gps.off_route';
export const TRACKING_APPROACHING_ROUTING_KEY = 'tracking.gps.approaching_stop';

export const OUTBOX_EVENT_ROUTING_KEYS: Record<string, string> = {
  [TRIP_DELAYED_EVENT_TYPE]: TRACKING_TRIP_DELAYED_ROUTING_KEY,
  [OFF_ROUTE_ALERT_EVENT_TYPE]: TRACKING_OFF_ROUTE_ROUTING_KEY,
  [APPROACHING_ALERT_EVENT_TYPE]: TRACKING_APPROACHING_ROUTING_KEY,
};
