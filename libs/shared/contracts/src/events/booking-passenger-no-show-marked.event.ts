import { z } from 'zod';

export const BOOKING_PASSENGER_NO_SHOW_MARKED_ROUTING_KEY =
  'booking.booking.passenger_no_show_marked';

const bookingPassengerNoShowMarkedEventSchema = z
  .object({
    eventId: z.string().uuid(),
    occurredAt: z.string().datetime({ offset: true }),
    eventType: z.literal(BOOKING_PASSENGER_NO_SHOW_MARKED_ROUTING_KEY),
    bookingId: z.string().uuid(),
    tripId: z.string().uuid(),
    userId: z.string().uuid(),
    bookingStatus: z.enum(['NO_SHOW', 'PARTIAL_NO_SHOW']),
    newlyNoShowPassengerIds: z.array(z.string().uuid()).min(1),
    triggerType: z.enum(['ALONG_ROUTE', 'TERMINAL']),
    pickupStopId: z.string().uuid().optional(),
  })
  .strict()
  .superRefine((event, ctx) => {
    const hasPickupStop = event.pickupStopId !== undefined;
    if (event.triggerType === 'ALONG_ROUTE' && !hasPickupStop) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'pickupStopId is required for an along-route trigger',
        path: ['pickupStopId'],
      });
    }
    if (event.triggerType === 'TERMINAL' && hasPickupStop) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'pickupStopId must be omitted for a terminal trigger',
        path: ['pickupStopId'],
      });
    }
  });

export type BookingPassengerNoShowMarkedEvent = z.infer<
  typeof bookingPassengerNoShowMarkedEventSchema
>;

export { bookingPassengerNoShowMarkedEventSchema as BookingPassengerNoShowMarkedEventSchema };
