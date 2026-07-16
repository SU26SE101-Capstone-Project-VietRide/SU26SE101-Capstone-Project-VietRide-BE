import { z } from 'zod';

export const TripVehicleSwapSeatImpactReasonSchema = z.enum([
  'SEAT_REMOVED',
  'SEAT_DISABLED',
  'SEAT_TYPE_DOWNGRADED',
]);
export type TripVehicleSwapSeatImpactReason = z.infer<typeof TripVehicleSwapSeatImpactReasonSchema>;

export const TripVehicleSwappedSeatImpactSchema = z
  .object({
    bookingId: z.string().uuid(),
    seatNumbers: z.array(z.string()),
    reason: TripVehicleSwapSeatImpactReasonSchema,
  })
  .strict();
export type TripVehicleSwappedSeatImpact = z.infer<typeof TripVehicleSwappedSeatImpactSchema>;

export const TripVehicleSwappedEventSchema = z
  .object({
    eventId: z.string().uuid(),
    occurredAt: z.string().datetime({ offset: true }),
    tripId: z.string().uuid(),
    operatorId: z.string().uuid(),
    oldVehicleId: z.string().uuid(),
    newVehicleId: z.string().uuid(),
    oldVehiclePlateNumber: z.string(),
    newVehiclePlateNumber: z.string(),
    departureDateTime: z.string().datetime({ offset: true }),
    driverUserId: z.string().uuid(),
    assistantUserId: z.string().uuid().nullable(),
    seatImpacts: z.array(TripVehicleSwappedSeatImpactSchema),
  })
  .strict();
export type TripVehicleSwappedEvent = z.infer<typeof TripVehicleSwappedEventSchema>;

export const TRIP_VEHICLE_SWAPPED_ROUTING_KEY = 'trip.trip.vehicle_swapped';
