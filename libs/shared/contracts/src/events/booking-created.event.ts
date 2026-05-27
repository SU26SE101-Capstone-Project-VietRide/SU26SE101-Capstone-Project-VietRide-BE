import { z } from 'zod';

export const GeoPointSchema = z.object({
  lat: z.number().gte(-90).lte(90),
  lng: z.number().gte(-180).lte(180),
  address: z.string().optional(),
});
export type GeoPoint = z.infer<typeof GeoPointSchema>;

export const BookingCreatedEventSchema = z.object({
  eventId: z.string().uuid(),
  occurredAt: z.string().datetime({ offset: true }),
  bookingId: z.string().uuid(),
  passengerId: z.string().uuid(),
  pickupLocation: GeoPointSchema,
  dropoffLocation: GeoPointSchema,
});

export type BookingCreatedEvent = z.infer<typeof BookingCreatedEventSchema>;

// <service>.<aggregate>.<verb_past> per BACKEND_SOURCE_OF_TRUTH §7.3.
export const BOOKING_CREATED_ROUTING_KEY = 'booking.booking.created';
