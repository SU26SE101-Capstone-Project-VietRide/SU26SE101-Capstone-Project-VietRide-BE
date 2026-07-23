import { z } from 'zod';

export const BOOKING_ROUTE_CHANGE_AUTO_FALLBACK_APPLIED_ROUTING_KEY =
  'booking.booking.route_change_auto_fallback_applied';

const bookingRouteChangeAutoFallbackAppliedEventSchema = z
  .object({
    eventId: z.string().uuid(),
    occurredAt: z.string().datetime({ offset: true }),
    eventType: z.literal(BOOKING_ROUTE_CHANGE_AUTO_FALLBACK_APPLIED_ROUTING_KEY),
    bookingId: z.string().uuid(),
    tripId: z.string().uuid(),
    userId: z.string().uuid(),
    pendingActionId: z.string().uuid(),
    originalStopId: z.string().uuid(),
    fallbackDestinationStationId: z.string().uuid(),
    shuttleRequired: z.literal(true),
    resolvedAction: z.literal('AUTO_FALLBACK_DESTINATION'),
  })
  .strict();

export type BookingRouteChangeAutoFallbackAppliedEvent = z.infer<
  typeof bookingRouteChangeAutoFallbackAppliedEventSchema
>;

export {
  bookingRouteChangeAutoFallbackAppliedEventSchema as BookingRouteChangeAutoFallbackAppliedEventSchema,
};
