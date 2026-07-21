import { z } from 'zod';

import { BookingRequiredScheduleChangeSeveritySchema } from './booking-schedule-change-required.event';

const bookingPendingActionAutoResolvedEventSchema = z
  .object({
    eventId: z.string().uuid(),
    occurredAt: z.string().datetime({ offset: true }),
    bookingId: z.string().uuid(),
    tripId: z.string().uuid(),
    userId: z.string().uuid(),
    pendingActionId: z.string().uuid(),
    resolvedAction: z.literal('ACCEPTED'),
    severity: BookingRequiredScheduleChangeSeveritySchema,
    oldDeparture: z.string().datetime({ offset: true }),
    newDeparture: z.string().datetime({ offset: true }),
  })
  .strict();
export type BookingPendingActionAutoResolvedEvent = z.infer<
  typeof bookingPendingActionAutoResolvedEventSchema
>;

export {
  bookingPendingActionAutoResolvedEventSchema as BookingPendingActionAutoResolvedEventSchema,
};

export const BOOKING_PENDING_ACTION_AUTO_RESOLVED_ROUTING_KEY =
  'booking.booking.pending_action_auto_resolved';
