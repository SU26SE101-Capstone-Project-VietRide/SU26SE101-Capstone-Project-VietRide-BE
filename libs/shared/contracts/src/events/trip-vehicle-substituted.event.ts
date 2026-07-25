/* eslint-disable @typescript-eslint/naming-convention -- Public Zod schemas follow contract naming. */
import { z } from 'zod';

export const TripVehicleSubstitutedBoardingStatusSchema = z.enum(['BOARDED', 'PENDING']);
export type TripVehicleSubstitutedBoardingStatus = z.infer<
  typeof TripVehicleSubstitutedBoardingStatusSchema
>;

export const TripVehicleSubstitutedMappingSchema = z
  .object({
    bookingId: z.string().uuid(),
    passengerId: z.string().uuid(),
    originalSeatNumber: z.string().nullable(),
    newSeatNumber: z.string().nullable(),
    originalBoardingStatus: TripVehicleSubstitutedBoardingStatusSchema,
  })
  .strict();
export type TripVehicleSubstitutedMapping = z.infer<
  typeof TripVehicleSubstitutedMappingSchema
>;

export const TripVehicleSubstitutedEventSchema = z
  .object({
    eventId: z.string().uuid(),
    occurredAt: z.string().datetime({ offset: true }),
    substitutionId: z.string().uuid(),
    disruptedAt: z.string().datetime({ offset: true }),
    operatorId: z.string().uuid(),
    oldTripId: z.string().uuid(),
    oldTripStatus: z.literal('DISRUPTED'),
    oldVehicleId: z.string().uuid(),
    newTripId: z.string().uuid(),
    newTripStatus: z.literal('BOARDING'),
    newVehicleId: z.string().uuid(),
    newVehiclePlateNumber: z.string(),
    newTripDepartureDateTime: z.string().datetime({ offset: true }),
    actorUserId: z.string().uuid(),
    reason: z.string(),
    notifyPassengers: z.boolean(),
    mappings: z.array(TripVehicleSubstitutedMappingSchema),
  })
  .strict()
  .superRefine((event, context) => {
    if (event.substitutionId !== event.eventId) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['substitutionId'],
        message: 'must equal eventId',
      });
    }

    if (event.occurredAt !== event.disruptedAt) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['occurredAt'],
        message: 'must equal disruptedAt',
      });
    }
  });
export type TripVehicleSubstitutedEvent = z.infer<typeof TripVehicleSubstitutedEventSchema>;

export const TRIP_VEHICLE_SUBSTITUTED_ROUTING_KEY = 'trip.trip.vehicle_substituted';
