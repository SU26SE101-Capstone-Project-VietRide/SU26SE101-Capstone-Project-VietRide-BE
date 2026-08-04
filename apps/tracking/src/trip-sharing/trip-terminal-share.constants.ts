import {
  TRIP_CANCELLED_ROUTING_KEY,
  TRIP_COMPLETED_ROUTING_KEY,
  TRIP_DISRUPTED_ROUTING_KEY,
  TripCancelledEventSchema,
  TripCompletedEventSchema,
  TripDisruptedEventSchema,
} from '@vietride/contracts';

export const TRIP_SHARE_TERMINAL_CONSUMER_OPTIONS = {
  prefetch: 1,
  deadLetter: true,
  maxRetries: 5,
  retryDelayMs: 10_000,
} as const;

export const TRIP_SHARE_TERMINAL_QUEUE_BINDINGS = [
  {
    queue: 'tracking-trip-share-completed',
    routingKey: TRIP_COMPLETED_ROUTING_KEY,
    schema: TripCompletedEventSchema,
  },
  {
    queue: 'tracking-trip-share-cancelled',
    routingKey: TRIP_CANCELLED_ROUTING_KEY,
    schema: TripCancelledEventSchema,
  },
  {
    queue: 'tracking-trip-share-disrupted',
    routingKey: TRIP_DISRUPTED_ROUTING_KEY,
    schema: TripDisruptedEventSchema,
  },
] as const;

export const TRIP_SHARE_EVENT_PROCESSED_TTL_SECONDS = 604_800;
export const TRIP_SHARE_EVENT_PROCESSING_TTL_SECONDS = 120;
