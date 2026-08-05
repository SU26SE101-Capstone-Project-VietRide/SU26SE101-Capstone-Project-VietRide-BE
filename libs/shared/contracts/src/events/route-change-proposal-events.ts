/* eslint-disable @typescript-eslint/naming-convention -- public schema exports follow established contract conventions. */
import { z } from 'zod';

export const RouteChangeProposalTypeSchema = z.enum(['EXISTING', 'CUSTOM']);
export type RouteChangeProposalType = z.infer<typeof RouteChangeProposalTypeSchema>;

export const RouteChangeProposalStatusSchema = z.enum([
  'PENDING',
  'APPROVED',
  'REJECTED',
  'SUPERSEDED',
  'EXPIRED',
]);
export type RouteChangeProposalStatus = z.infer<typeof RouteChangeProposalStatusSchema>;

export const RouteChangeProposalResolutionCodeSchema = z.enum([
  'ANOTHER_PROPOSAL_APPROVED',
  'ROUTE_CHANGED_DIRECTLY',
  'TRIP_NO_LONGER_EDITABLE',
  'SOURCE_ROUTE_CHANGED',
]);
export type RouteChangeProposalResolutionCode = z.infer<
  typeof RouteChangeProposalResolutionCodeSchema
>;

const nullableUuid = z.string().uuid().nullable();
const nullableResolutionCode = RouteChangeProposalResolutionCodeSchema.nullable();

const routeChangeProposalEventSchema = z
  .object({
    eventId: z.string().uuid(),
    occurredAt: z.string().datetime({ offset: true }),
    proposalId: z.string().uuid(),
    tripId: z.string().uuid(),
    operatorId: z.string().uuid(),
    proposedByUserId: z.string().uuid(),
    actorUserId: nullableUuid,
    proposalType: RouteChangeProposalTypeSchema,
    status: RouteChangeProposalStatusSchema,
    sourceAlternativeRouteId: nullableUuid,
    approvedAlternativeRouteId: nullableUuid,
    incidentId: nullableUuid,
    reason: z.string().trim().min(1).max(500),
    rejectionReason: z.string().max(500).nullable(),
    resolutionCode: nullableResolutionCode,
    supersededByProposalId: nullableUuid,
  })
  .strict();

export const RouteChangeProposalCreatedEventSchema = routeChangeProposalEventSchema
  .extend({
    actorUserId: z.string().uuid(),
    status: z.literal('PENDING'),
  })
  .superRefine((event, context) => {
    if (event.actorUserId !== event.proposedByUserId) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['actorUserId'],
        message: 'must equal proposedByUserId for a created proposal',
      });
    }
  });
export type RouteChangeProposalCreatedEvent = z.infer<typeof RouteChangeProposalCreatedEventSchema>;

export const RouteChangeProposalApprovedEventSchema = routeChangeProposalEventSchema.extend({
  actorUserId: z.string().uuid(),
  status: z.literal('APPROVED'),
  approvedAlternativeRouteId: z.string().uuid(),
});
export type RouteChangeProposalApprovedEvent = z.infer<
  typeof RouteChangeProposalApprovedEventSchema
>;

export const RouteChangeProposalRejectedEventSchema = routeChangeProposalEventSchema.extend({
  actorUserId: z.string().uuid(),
  status: z.literal('REJECTED'),
});
export type RouteChangeProposalRejectedEvent = z.infer<
  typeof RouteChangeProposalRejectedEventSchema
>;

export const RouteChangeProposalSupersededEventSchema = routeChangeProposalEventSchema
  .extend({
    status: z.literal('SUPERSEDED'),
    resolutionCode: z.enum(['ANOTHER_PROPOSAL_APPROVED', 'ROUTE_CHANGED_DIRECTLY']),
  })
  .superRefine((event, context) => {
    const winnerExpected = event.resolutionCode === 'ANOTHER_PROPOSAL_APPROVED';
    if (winnerExpected === (event.supersededByProposalId === null)) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['supersededByProposalId'],
        message:
          'must be a winner proposal id only when resolutionCode is ANOTHER_PROPOSAL_APPROVED',
      });
    }
  });
export type RouteChangeProposalSupersededEvent = z.infer<
  typeof RouteChangeProposalSupersededEventSchema
>;

export const RouteChangeProposalExpiredEventSchema = routeChangeProposalEventSchema.extend({
  actorUserId: z.null(),
  status: z.literal('EXPIRED'),
  resolutionCode: z.enum(['TRIP_NO_LONGER_EDITABLE', 'SOURCE_ROUTE_CHANGED']),
});
export type RouteChangeProposalExpiredEvent = z.infer<typeof RouteChangeProposalExpiredEventSchema>;

export type RouteChangeProposalEvent =
  | RouteChangeProposalCreatedEvent
  | RouteChangeProposalApprovedEvent
  | RouteChangeProposalRejectedEvent
  | RouteChangeProposalSupersededEvent
  | RouteChangeProposalExpiredEvent;

export const TRIP_ROUTE_CHANGE_PROPOSAL_CREATED_ROUTING_KEY = 'trip.route_change_proposal.created';
export const TRIP_ROUTE_CHANGE_PROPOSAL_APPROVED_ROUTING_KEY =
  'trip.route_change_proposal.approved';
export const TRIP_ROUTE_CHANGE_PROPOSAL_REJECTED_ROUTING_KEY =
  'trip.route_change_proposal.rejected';
export const TRIP_ROUTE_CHANGE_PROPOSAL_SUPERSEDED_ROUTING_KEY =
  'trip.route_change_proposal.superseded';
export const TRIP_ROUTE_CHANGE_PROPOSAL_EXPIRED_ROUTING_KEY = 'trip.route_change_proposal.expired';

export type RouteChangeProposalRoutingKey =
  | typeof TRIP_ROUTE_CHANGE_PROPOSAL_CREATED_ROUTING_KEY
  | typeof TRIP_ROUTE_CHANGE_PROPOSAL_APPROVED_ROUTING_KEY
  | typeof TRIP_ROUTE_CHANGE_PROPOSAL_REJECTED_ROUTING_KEY
  | typeof TRIP_ROUTE_CHANGE_PROPOSAL_SUPERSEDED_ROUTING_KEY
  | typeof TRIP_ROUTE_CHANGE_PROPOSAL_EXPIRED_ROUTING_KEY;
