import { z } from 'zod';

export const BOOKING_STOP_DISABLED_AUTO_FALLBACK_APPLIED_ROUTING_KEY =
  'booking.booking.stop_disabled_auto_fallback_applied';

const bookingStopDisabledAutoFallbackAppliedEventSchema = z
  .object({
    eventId: z.string().uuid(),
    occurredAt: z.string().datetime({ offset: true }),
    eventType: z.literal(BOOKING_STOP_DISABLED_AUTO_FALLBACK_APPLIED_ROUTING_KEY),
    bookingId: z.string().uuid(),
    tripId: z.string().uuid(),
    userId: z.string().uuid(),
    pendingActionId: z.string().uuid(),
    disabledStopId: z.string().uuid(),
    affectedField: z.enum(['PICKUP', 'DROPOFF']),
    fallbackStationId: z.string().uuid(),
    resolvedAction: z.literal('AUTO_FALLBACK_DESTINATION'),
  })
  .strict();

export type BookingStopDisabledAutoFallbackAppliedEvent = z.infer<
  typeof bookingStopDisabledAutoFallbackAppliedEventSchema
>;

export { bookingStopDisabledAutoFallbackAppliedEventSchema as BookingStopDisabledAutoFallbackAppliedEventSchema };
