import {
  TRIP_ASSIGNMENT_START_BLOCKED_ROUTING_KEY,
  TripAssignmentStartBlockedEventSchema,
} from '../trip-assignment-events';

describe('trip assignment events', () => {
  it('freezes the start-blocked routing key and payload', () => {
    expect(TRIP_ASSIGNMENT_START_BLOCKED_ROUTING_KEY).toBe('trip.assignment.start_blocked');
    expect(
      TripAssignmentStartBlockedEventSchema.parse({
        eventId: '10000000-0000-4000-8000-000000000001',
        occurredAt: '2026-08-11T01:00:00Z',
        tripId: '10000000-0000-4000-8000-000000000002',
        operatorId: '10000000-0000-4000-8000-000000000003',
        resourceRole: 'DRIVER',
        resourceId: '10000000-0000-4000-8000-000000000004',
        conflictingSourceType: 'SHUTTLE_TRIP',
        conflictingSourceId: '10000000-0000-4000-8000-000000000005',
        conflictReason: 'RESOURCE_ACTIVE',
        blockingUntil: null,
      }),
    ).toBeDefined();
  });
});
