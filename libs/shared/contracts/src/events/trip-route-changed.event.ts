/* eslint-disable @typescript-eslint/naming-convention -- public event schema export follows contract conventions. */
import { z } from 'zod';

const tripRouteChangedCandidateStopSchema = z
  .object({
    stopId: z.string().uuid().nullable(),
    stationId: z.string().uuid().nullable(),
    stationName: z.string().trim().min(1),
    sequence: z.number().int().positive(),
    estimatedArrivalAt: z.string().datetime({ offset: true }),
  })
  .strict()
  .superRefine((candidate, ctx) => {
    if ((candidate.stopId === null) === (candidate.stationId === null)) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'Exactly one of stopId or stationId is required',
        path: ['stopId'],
      });
    }
  });

const tripRouteChangedAffectedBookingSchema = z
  .object({
    bookingId: z.string().uuid(),
    candidateStops: z.array(tripRouteChangedCandidateStopSchema).min(1),
  })
  .strict()
  .superRefine((booking, ctx) => {
    for (let index = 1; index < booking.candidateStops.length; index += 1) {
      const current = booking.candidateStops[index];
      const previous = booking.candidateStops[index - 1];
      if (current && previous && current.sequence <= previous.sequence) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          message: 'candidateStops must be ordered by sequence',
          path: ['candidateStops', index, 'sequence'],
        });
      }
    }
  });

export const TripRouteChangedEventSchema = z
  .object({
    eventId: z.string().uuid(),
    occurredAt: z.string().datetime({ offset: true }),
    tripId: z.string().uuid(),
    operatorId: z.string().uuid(),
    tripStatus: z.enum(['SCHEDULED', 'BOARDING', 'IN_PROGRESS']),
    alternativeRouteId: z.string().uuid(),
    affectedBookings: z.array(tripRouteChangedAffectedBookingSchema),
  })
  .strict();

export type TripRouteChangedEvent = z.infer<typeof TripRouteChangedEventSchema>;

export const TRIP_ROUTE_CHANGED_ROUTING_KEY = 'trip.trip.route_changed';
