import { z } from 'zod';

export const BOOKING_STOP_DISABLED_AFFECTED_ROUTING_KEY = 'booking.stop_disabled.affected';

const bookingStopDisabledAffectedEventSchema = z
  .object({
    eventId: z.string().uuid(),
    occurredAt: z.string().datetime({ offset: true }),
    eventType: z.literal(BOOKING_STOP_DISABLED_AFFECTED_ROUTING_KEY),
    stopId: z.string().uuid(),
    replacedByStopId: z.string().uuid().optional(),
    recipientUserIds: z.array(z.string().uuid()).min(1),
    affectedBookingCount: z.number().int().positive(),
  })
  .strict()
  .superRefine((event, ctx) => {
    if (new Set(event.recipientUserIds).size !== event.recipientUserIds.length) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'recipientUserIds must be deduplicated',
        path: ['recipientUserIds'],
      });
    }
  });

export type BookingStopDisabledAffectedEvent = z.infer<
  typeof bookingStopDisabledAffectedEventSchema
>;

export { bookingStopDisabledAffectedEventSchema as BookingStopDisabledAffectedEventSchema };
