import { z } from 'zod';

export const TripScheduleChangeSeveritySchema = z.enum(['MINOR', 'MEDIUM', 'MAJOR']);
export type TripScheduleChangeSeverity = z.infer<typeof TripScheduleChangeSeveritySchema>;

export const TripScheduleChangedEventSchema = z
  .object({
    eventId: z.string().uuid(),
    occurredAt: z.string().datetime({ offset: true }),
    tripId: z.string().uuid(),
    operatorId: z.string().uuid(),
    oldDeparture: z.string().datetime({ offset: true }),
    newDeparture: z.string().datetime({ offset: true }),
    severity: TripScheduleChangeSeveritySchema,
  })
  .strict();
export type TripScheduleChangedEvent = z.infer<typeof TripScheduleChangedEventSchema>;

export const TRIP_SCHEDULE_CHANGED_ROUTING_KEY = 'trip.trip.schedule_changed';
