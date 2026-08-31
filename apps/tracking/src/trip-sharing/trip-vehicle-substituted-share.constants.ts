import {
  TRIP_VEHICLE_SUBSTITUTED_ROUTING_KEY,
  TripVehicleSubstitutedEventSchema,
} from '@vietride/contracts';

export const TRIP_SHARE_VEHICLE_SUBSTITUTED_QUEUE =
  'tracking-trip-share-vehicle-substituted';
export { TRIP_VEHICLE_SUBSTITUTED_ROUTING_KEY };

export const TripShareVehicleSubstitutedEventSchema =
  TripVehicleSubstitutedEventSchema.refine(
    (event) => event.oldTripId !== event.newTripId,
    { path: ['newTripId'], message: 'must differ from oldTripId' },
  );

export const TRIP_SHARE_VEHICLE_SUBSTITUTED_CONSUMER_OPTIONS = {
  prefetch: 1,
  deadLetter: true,
  maxRetries: 5,
  retryDelayMs: 10_000,
} as const;
