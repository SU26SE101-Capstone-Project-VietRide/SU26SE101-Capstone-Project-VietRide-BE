import {
  RouteChangeProposalApprovedEventSchema,
  RouteChangeProposalCreatedEventSchema,
  RouteChangeProposalExpiredEventSchema,
  RouteChangeProposalRejectedEventSchema,
  RouteChangeProposalSupersededEventSchema,
  TRIP_ROUTE_CHANGE_PROPOSAL_APPROVED_ROUTING_KEY,
  TRIP_ROUTE_CHANGE_PROPOSAL_CREATED_ROUTING_KEY,
  TRIP_ROUTE_CHANGE_PROPOSAL_EXPIRED_ROUTING_KEY,
  TRIP_ROUTE_CHANGE_PROPOSAL_REJECTED_ROUTING_KEY,
  TRIP_ROUTE_CHANGE_PROPOSAL_SUPERSEDED_ROUTING_KEY,
} from '../../index';

describe('route-change proposal integration contracts', () => {
  it.each([
    [
      TRIP_ROUTE_CHANGE_PROPOSAL_CREATED_ROUTING_KEY,
      RouteChangeProposalCreatedEventSchema,
      'PENDING',
    ],
    [
      TRIP_ROUTE_CHANGE_PROPOSAL_APPROVED_ROUTING_KEY,
      RouteChangeProposalApprovedEventSchema,
      'APPROVED',
    ],
    [
      TRIP_ROUTE_CHANGE_PROPOSAL_REJECTED_ROUTING_KEY,
      RouteChangeProposalRejectedEventSchema,
      'REJECTED',
    ],
    [
      TRIP_ROUTE_CHANGE_PROPOSAL_SUPERSEDED_ROUTING_KEY,
      RouteChangeProposalSupersededEventSchema,
      'SUPERSEDED',
    ],
    [
      TRIP_ROUTE_CHANGE_PROPOSAL_EXPIRED_ROUTING_KEY,
      RouteChangeProposalExpiredEventSchema,
      'EXPIRED',
    ],
  ] as const)('freezes %s with status %s', (routingKey, schema, status) => {
    expect(routingKey).toBe(`trip.route_change_proposal.${routingKey.split('.').at(-1)}`);
    expect(schema.parse(validEvent(status))).toEqual(validEvent(status));
  });

  it('accepts a SUPERSEDED event when no actor is available', () => {
    const event = { ...validEvent('SUPERSEDED'), actorUserId: null };

    expect(RouteChangeProposalSupersededEventSchema.parse(event)).toEqual(event);
  });

  it.each([
    ['missing field', without(validEvent('PENDING'), 'operatorId')],
    ['extra field', { ...validEvent('PENDING'), legacyStatus: 'OPEN' }],
    ['wrong status', { ...validEvent('PENDING'), status: 'APPROVED' }],
    ['wrong proposal type', { ...validEvent('PENDING'), proposalType: 'OTHER' }],
    ['unknown resolution code', { ...validEvent('PENDING'), resolutionCode: 'UNKNOWN' }],
  ])('rejects a %s', (_caseName, payload) => {
    expect(RouteChangeProposalCreatedEventSchema.safeParse(payload).success).toBe(false);
  });

  it.each([
    [
      'CREATED actor differs from proposer',
      RouteChangeProposalCreatedEventSchema,
      { ...validEvent('PENDING'), actorUserId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa' },
    ],
    [
      'APPROVED actor is null',
      RouteChangeProposalApprovedEventSchema,
      { ...validEvent('APPROVED'), actorUserId: null },
    ],
    [
      'APPROVED route is null',
      RouteChangeProposalApprovedEventSchema,
      { ...validEvent('APPROVED'), approvedAlternativeRouteId: null },
    ],
    [
      'REJECTED actor is null',
      RouteChangeProposalRejectedEventSchema,
      { ...validEvent('REJECTED'), actorUserId: null },
    ],
    [
      'SUPERSEDED resolution belongs to EXPIRED',
      RouteChangeProposalSupersededEventSchema,
      { ...validEvent('SUPERSEDED'), resolutionCode: 'SOURCE_ROUTE_CHANGED' },
    ],
    [
      'SUPERSEDED winner is absent for approval',
      RouteChangeProposalSupersededEventSchema,
      { ...validEvent('SUPERSEDED'), supersededByProposalId: null },
    ],
    [
      'SUPERSEDED direct change carries a winner',
      RouteChangeProposalSupersededEventSchema,
      { ...validEvent('SUPERSEDED'), resolutionCode: 'ROUTE_CHANGED_DIRECTLY' },
    ],
    [
      'EXPIRED actor is present',
      RouteChangeProposalExpiredEventSchema,
      { ...validEvent('EXPIRED'), actorUserId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa' },
    ],
    [
      'EXPIRED resolution belongs to SUPERSEDED',
      RouteChangeProposalExpiredEventSchema,
      { ...validEvent('EXPIRED'), resolutionCode: 'ANOTHER_PROPOSAL_APPROVED' },
    ],
  ] as const)('rejects lifecycle mismatch: %s', (_caseName, schema, payload) => {
    expect(schema.safeParse(payload).success).toBe(false);
  });
});

function validEvent(status: string): Record<string, unknown> {
  return {
    eventId: '11111111-1111-4111-8111-111111111111',
    occurredAt: '2026-08-04T10:00:00+07:00',
    proposalId: '22222222-2222-4222-8222-222222222222',
    tripId: '33333333-3333-4333-8333-333333333333',
    operatorId: '44444444-4444-4444-8444-444444444444',
    proposedByUserId: '55555555-5555-4555-8555-555555555555',
    actorUserId:
      status === 'EXPIRED'
        ? null
        : status === 'PENDING'
          ? '55555555-5555-4555-8555-555555555555'
          : '66666666-6666-4666-8666-666666666666',
    proposalType: 'CUSTOM',
    status,
    sourceAlternativeRouteId: null,
    approvedAlternativeRouteId:
      status === 'APPROVED' ? '77777777-7777-4777-8777-777777777777' : null,
    incidentId: null,
    reason: 'Road obstruction',
    rejectionReason: null,
    resolutionCode:
      status === 'SUPERSEDED'
        ? 'ANOTHER_PROPOSAL_APPROVED'
        : status === 'EXPIRED'
          ? 'SOURCE_ROUTE_CHANGED'
          : null,
    supersededByProposalId: status === 'SUPERSEDED' ? '88888888-8888-4888-8888-888888888888' : null,
  };
}

function without<T extends object, TKey extends keyof T>(value: T, key: TKey): Omit<T, TKey> {
  const copy = { ...value };
  delete copy[key];
  return copy;
}
