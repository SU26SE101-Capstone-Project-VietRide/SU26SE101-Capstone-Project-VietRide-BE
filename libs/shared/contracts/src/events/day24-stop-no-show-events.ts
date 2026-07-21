import { z } from 'zod';
import { BookingPassengerNoShowMarkedEventSchema } from './booking-passenger-no-show-marked.event';
import { BookingStopDisabledAffectedEventSchema } from './booking-stop-disabled-affected.event';
import { BookingStopDisabledAutoFallbackAppliedEventSchema } from './booking-stop-disabled-auto-fallback-applied.event';
import { TripStopDepartedWithPendingEventSchema } from './trip-stop-departed-with-pending.event';

const day24StopNoShowEventSchema = z.union([
  BookingStopDisabledAffectedEventSchema,
  BookingStopDisabledAutoFallbackAppliedEventSchema,
  BookingPassengerNoShowMarkedEventSchema,
  TripStopDepartedWithPendingEventSchema,
]);

export type Day24StopNoShowEvent = z.infer<typeof day24StopNoShowEventSchema>;

export { day24StopNoShowEventSchema as Day24StopNoShowEventSchema };
