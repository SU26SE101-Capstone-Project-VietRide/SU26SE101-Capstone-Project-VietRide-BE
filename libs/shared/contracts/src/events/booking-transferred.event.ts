/* eslint-disable @typescript-eslint/naming-convention -- Public Zod schemas follow contract naming. */
import { z } from 'zod';

export const BookingTransferConfirmationStatusSchema = z.enum([
  'PENDING_CONFIRM',
  'CONFIRMED',
  'NOT_REQUIRED',
]);
export type BookingTransferConfirmationStatus = z.infer<
  typeof BookingTransferConfirmationStatusSchema
>;

export const BookingTransferredItemSchema = z
  .object({
    passengerId: z.string().uuid(),
    originalSeatNumber: z.string().nullable(),
    newSeatNumber: z.string().nullable(),
    confirmationStatus: BookingTransferConfirmationStatusSchema,
  })
  .strict();
export type BookingTransferredItem = z.infer<typeof BookingTransferredItemSchema>;

export const BookingTransferredEventSchema = z
  .object({
    eventId: z.string().uuid(),
    occurredAt: z.string().datetime({ offset: true }),
    sourceSubstitutionEventId: z.string().uuid(),
    bookingId: z.string().uuid(),
    recipientUserId: z.string().uuid(),
    operatorId: z.string().uuid(),
    oldTripId: z.string().uuid(),
    newTripId: z.string().uuid(),
    newVehicleId: z.string().uuid(),
    newVehiclePlateNumber: z.string(),
    newTripDepartureDateTime: z.string().datetime({ offset: true }),
    notifyPassengers: z.boolean(),
    transfers: z.array(BookingTransferredItemSchema),
  })
  .strict();
export type BookingTransferredEvent = z.infer<typeof BookingTransferredEventSchema>;

export const BOOKING_TRANSFERRED_ROUTING_KEY = 'booking.booking.transferred';
