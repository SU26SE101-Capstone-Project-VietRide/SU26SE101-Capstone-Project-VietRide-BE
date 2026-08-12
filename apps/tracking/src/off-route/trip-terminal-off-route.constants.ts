import {
  TRIP_CANCELLED_ROUTING_KEY,
  TRIP_COMPLETED_ROUTING_KEY,
  TRIP_DISRUPTED_ROUTING_KEY,
  TripCancelledEventSchema,
  TripCompletedEventSchema,
  TripDisruptedEventSchema,
} from '@vietride/contracts';

export const TRIP_TERMINAL_OFF_ROUTE_CONSUMER_OPTIONS = {
  prefetch: 1,
  deadLetter: true,
  maxRetries: 5,
  retryDelayMs: 10_000,
} as const;

export const TRIP_TERMINAL_OFF_ROUTE_QUEUE_BINDINGS = [
  {
    queue: 'tracking-off-route-trip-completed',
    routingKey: TRIP_COMPLETED_ROUTING_KEY,
    schema: TripCompletedEventSchema,
  },
  {
    queue: 'tracking-off-route-trip-cancelled',
    routingKey: TRIP_CANCELLED_ROUTING_KEY,
    schema: TripCancelledEventSchema,
  },
  {
    queue: 'tracking-off-route-trip-disrupted',
    routingKey: TRIP_DISRUPTED_ROUTING_KEY,
    schema: TripDisruptedEventSchema,
  },
] as const;

export const TRIP_TERMINAL_OFF_ROUTE_PROCESSED_TTL_SECONDS = 604_800;
export const TRIP_TERMINAL_OFF_ROUTE_PROCESSING_TTL_SECONDS = 120;
