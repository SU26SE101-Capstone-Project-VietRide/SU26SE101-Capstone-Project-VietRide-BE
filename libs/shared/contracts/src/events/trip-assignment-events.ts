import { z } from 'zod';

export const TRIP_ASSIGNMENT_START_BLOCKED_ROUTING_KEY = 'trip.assignment.start_blocked';

export const TripAssignmentStartBlockedEventSchema = z.object({
  eventId: z.string().uuid(),
  occurredAt: z.string(),
  tripId: z.string().uuid(),
  operatorId: z.string().uuid(),
  resourceRole: z.enum(['DRIVER', 'ASSISTANT', 'VEHICLE']),
  resourceId: z.string().uuid(),
  conflictingSourceType: z.enum(['TRIP', 'SHUTTLE_TRIP']),
  conflictingSourceId: z.string().uuid(),
  conflictReason: z.literal('RESOURCE_ACTIVE'),
  blockingUntil: z.string().nullable(),
}).strict();

export type TripAssignmentStartBlockedEvent = z.infer<
  typeof TripAssignmentStartBlockedEventSchema
>;
