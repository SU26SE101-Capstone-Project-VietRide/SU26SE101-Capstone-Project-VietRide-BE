import { z } from 'zod';

export const TRIP_STOP_DEPARTED_WITH_PENDING_ROUTING_KEY = 'trip.stop.departed_with_pending';

const tripStopDepartedWithPendingEventSchema = z
  .object({
    eventId: z.string().uuid(),
    occurredAt: z.string().datetime({ offset: true }),
    eventType: z.literal(TRIP_STOP_DEPARTED_WITH_PENDING_ROUTING_KEY),
    tripId: z.string().uuid(),
    stopId: z.string().uuid(),
    stopName: z.string().trim().min(1),
    pendingPassengerCount: z.number().int().positive(),
    driverUserId: z.string().uuid(),
    assistantUserId: z.string().uuid().nullable(),
    departedAt: z.string().datetime({ offset: true }),
  })
  .strict();

export type TripStopDepartedWithPendingEvent = z.infer<
  typeof tripStopDepartedWithPendingEventSchema
>;

export { tripStopDepartedWithPendingEventSchema as TripStopDepartedWithPendingEventSchema };
