import { z } from 'zod';

export const TRIP_SHUTTLE_ASSIGNED_ROUTING_KEY = 'trip.shuttle.assigned';
export const TRIP_SHUTTLE_WARNING_ROUTING_KEY = 'trip.shuttle.warning_issued';
export const TRIP_SHUTTLE_UNFULFILLED_ROUTING_KEY = 'trip.shuttle.unfulfilled';
export const TRIP_SHUTTLE_CANCELLED_ROUTING_KEY = 'trip.shuttle.cancelled';
export const TRIP_SHUTTLE_PICKED_UP_ROUTING_KEY = 'trip.shuttle.picked_up';
export const TRIP_SHUTTLE_DELIVERED_ROUTING_KEY = 'trip.shuttle.delivered';
export const TRIP_SHUTTLE_NO_SHOW_ROUTING_KEY = 'trip.shuttle.no_show';
export const TRIP_SHUTTLE_COMPLETED_ROUTING_KEY = 'trip.shuttle.completed';

const directionSchema = z.enum(['INBOUND_TO_STATION', 'OUTBOUND_FROM_STATION']);

const tripShuttleAssignedEventSchema = z.object({
  eventId: z.string().uuid().optional(),
  shuttleTripId: z.string().uuid(),
  mainTripId: z.string().uuid(),
  operatorId: z.string().uuid().optional(),
  bookingId: z.string().uuid(),
  passengerUserId: z.string().uuid(),
  direction: directionSchema,
  ticketIds: z.array(z.string().uuid()).min(1),
  pickupOrder: z.number().int().positive(),
  scheduledDepartureTime: z.string(),
  scheduledEndTime: z.string(),
  driver: z.object({
    userId: z.string().uuid(),
    displayName: z.string().trim().min(1),
    phone: z.string().trim().min(1),
  }),
  vehicle: z.object({ id: z.string().uuid(), licensePlate: z.string().trim().min(1) }),
}).strict();

export type TripShuttleAssignedEvent = z.infer<typeof tripShuttleAssignedEventSchema>;
export { tripShuttleAssignedEventSchema as TripShuttleAssignedEventSchema };

const tripShuttleWarningEventSchema = z.object({
  eventId: z.string().uuid().optional(),
  mainTripId: z.string().uuid(),
  operatorId: z.string().uuid(),
  alertType: z.enum(['WARNING_120', 'WARNING_60']),
  pendingBookingCount: z.number().int().nonnegative(),
  pendingPassengerCount: z.number().int().nonnegative(),
  hardCutoffAt: z.string(),
}).strict();

export type TripShuttleWarningEvent = z.infer<typeof tripShuttleWarningEventSchema>;
export { tripShuttleWarningEventSchema as TripShuttleWarningEventSchema };

const tripShuttleUnfulfilledEventSchema = z.object({
  eventId: z.string().uuid().optional(),
  mainTripId: z.string().uuid(),
  bookingId: z.string().uuid(),
  passengerUserId: z.string().uuid(),
  stationId: z.string().uuid(),
  reason: z.literal('AUTO_UNFULFILLED_CUTOFF'),
}).strict();

export type TripShuttleUnfulfilledEvent = z.infer<typeof tripShuttleUnfulfilledEventSchema>;
export { tripShuttleUnfulfilledEventSchema as TripShuttleUnfulfilledEventSchema };

const tripShuttleLifecycleEventSchema = z.object({
  eventId: z.string().uuid(),
  occurredAt: z.string().optional(),
  shuttleTripId: z.string().uuid().nullable(),
  mainTripId: z.string().uuid(),
  operatorId: z.string().uuid(),
  bookingId: z.string().uuid().nullable(),
  passengerUserId: z.string().uuid().nullable().optional(),
  direction: directionSchema,
  serviceAddress: z.string().optional(),
  serviceOrder: z.number().int().positive().nullable().optional(),
  status: z.string(),
  roadDistanceMeters: z.number().int().nonnegative().nullable().optional(),
  reason: z.string().nullable().optional(),
}).strict();

export type TripShuttleLifecycleEvent = z.infer<typeof tripShuttleLifecycleEventSchema>;
export { tripShuttleLifecycleEventSchema as TripShuttleLifecycleEventSchema };
