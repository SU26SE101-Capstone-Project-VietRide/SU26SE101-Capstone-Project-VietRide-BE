import { z } from 'zod';

export const GeoPointSchema = z.object({
  lat: z.number().gte(-90).lte(90),
  lng: z.number().gte(-180).lte(180),
  address: z.string().optional(),
});
export type GeoPoint = z.infer<typeof GeoPointSchema>;

export const LegacyBookingCreatedEventSchema = z.object({
  eventId: z.string().uuid(),
  occurredAt: z.string().datetime({ offset: true }),
  bookingId: z.string().uuid(),
  passengerId: z.string().uuid(),
  pickupLocation: GeoPointSchema,
  dropoffLocation: GeoPointSchema,
});
export type LegacyBookingCreatedEvent = z.infer<typeof LegacyBookingCreatedEventSchema>;

export const BookingLocationSchema = z
  .object({
    stationId: z.string().uuid().nullable(),
    stopId: z.string().uuid().nullable(),
    address: z.string().trim().min(1).nullable(),
  })
  .strict();
export type BookingLocation = z.infer<typeof BookingLocationSchema>;

export const OperationalBookingCreatedEventSchema = z
  .object({
    eventId: z.string().uuid(),
    occurredAt: z.string().datetime({ offset: true }),
    bookingId: z.string().uuid(),
    bookingCode: z.string().trim().min(1),
    tripId: z.string().uuid(),
    status: z.literal('CONFIRMED'),
    ticketCodes: z.array(z.string().trim().min(1)).min(1),
    passengerCount: z.number().int().positive(),
    pickup: BookingLocationSchema,
    dropoff: BookingLocationSchema,
    driverUserId: z.string().uuid(),
    assistantUserId: z.string().uuid().nullable(),
  })
  .strict()
  .refine((event) => event.passengerCount === event.ticketCodes.length, {
    message: 'passengerCount must equal ticketCodes length',
    path: ['passengerCount'],
  });
export type OperationalBookingCreatedEvent = z.infer<
  typeof OperationalBookingCreatedEventSchema
>;

export const BookingCreatedEventSchema = z.union([
  OperationalBookingCreatedEventSchema,
  LegacyBookingCreatedEventSchema,
]);

export type BookingCreatedEvent = z.infer<typeof BookingCreatedEventSchema>;

// <service>.<aggregate>.<verb_past> per BACKEND_SOURCE_OF_TRUTH §7.3.
export const BOOKING_CREATED_ROUTING_KEY = 'booking.booking.created';
