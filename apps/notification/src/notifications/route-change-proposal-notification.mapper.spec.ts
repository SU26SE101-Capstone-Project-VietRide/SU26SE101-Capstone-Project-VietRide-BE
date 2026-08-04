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
} from '@vietride/contracts';
import { NotificationType } from '../generated/notification-prisma-client';
import { mapRouteChangeProposalToNotifications } from './route-change-proposal-notification.mapper';

const RECIPIENT_ID = '11111111-1111-4111-8111-111111111111';
const PROPOSER_ID = '22222222-2222-4222-8222-222222222222';

describe('route-change proposal notification mapper', () => {
  it('fans CREATED out to unique operator admins with proposal context', () => {
    const event = RouteChangeProposalCreatedEventSchema.parse(eventPayload('PENDING'));

    const notifications = mapRouteChangeProposalToNotifications(
      TRIP_ROUTE_CHANGE_PROPOSAL_CREATED_ROUTING_KEY,
      event,
      [RECIPIENT_ID, RECIPIENT_ID],
    );

    expect(notifications).toHaveLength(1);
    expect(notifications[0]).toMatchObject({
      userId: RECIPIENT_ID,
      type: NotificationType.ROUTE_CHANGE_PROPOSAL_CREATED,
      data: {
        proposalId: event.proposalId,
        tripId: event.tripId,
        status: 'PENDING',
        reason: event.reason,
        rejectionReason: null,
        resolutionCode: null,
        supersededByProposalId: null,
      },
    });
  });

  it.each([
    [
      TRIP_ROUTE_CHANGE_PROPOSAL_APPROVED_ROUTING_KEY,
      RouteChangeProposalApprovedEventSchema,
      'APPROVED',
      NotificationType.ROUTE_CHANGE_PROPOSAL_APPROVED,
    ],
    [
      TRIP_ROUTE_CHANGE_PROPOSAL_REJECTED_ROUTING_KEY,
      RouteChangeProposalRejectedEventSchema,
      'REJECTED',
      NotificationType.ROUTE_CHANGE_PROPOSAL_REJECTED,
    ],
    [
      TRIP_ROUTE_CHANGE_PROPOSAL_SUPERSEDED_ROUTING_KEY,
      RouteChangeProposalSupersededEventSchema,
      'SUPERSEDED',
      NotificationType.ROUTE_CHANGE_PROPOSAL_SUPERSEDED,
    ],
    [
      TRIP_ROUTE_CHANGE_PROPOSAL_EXPIRED_ROUTING_KEY,
      RouteChangeProposalExpiredEventSchema,
      'EXPIRED',
      NotificationType.ROUTE_CHANGE_PROPOSAL_EXPIRED,
    ],
  ] as const)('maps terminal %s directly to the proposer', (routingKey, schema, status, type) => {
    const event = schema.parse(eventPayload(status));

    const [notification] = mapRouteChangeProposalToNotifications(routingKey, event, [RECIPIENT_ID]);

    expect(notification).toMatchObject({
      userId: PROPOSER_ID,
      type,
      data: {
        proposalId: event.proposalId,
        tripId: event.tripId,
        status,
        reason: event.reason,
        rejectionReason: event.rejectionReason,
        resolutionCode: event.resolutionCode,
        supersededByProposalId: event.supersededByProposalId,
      },
    });
  });
});

function eventPayload(status: string): Record<string, unknown> {
  return {
    eventId: '33333333-3333-4333-8333-333333333333',
    occurredAt: '2026-08-04T03:00:00Z',
    proposalId: '44444444-4444-4444-8444-444444444444',
    tripId: '55555555-5555-4555-8555-555555555555',
    operatorId: '66666666-6666-4666-8666-666666666666',
    proposedByUserId: PROPOSER_ID,
    actorUserId:
      status === 'EXPIRED'
        ? null
        : status === 'PENDING'
          ? PROPOSER_ID
          : '77777777-7777-4777-8777-777777777777',
    proposalType: 'EXISTING',
    status,
    sourceAlternativeRouteId: '88888888-8888-4888-8888-888888888888',
    approvedAlternativeRouteId:
      status === 'APPROVED' ? '99999999-9999-4999-8999-999999999999' : null,
    incidentId: null,
    reason: 'Road obstruction',
    rejectionReason: status === 'REJECTED' ? 'Use the planned route' : null,
    resolutionCode:
      status === 'SUPERSEDED'
        ? 'ANOTHER_PROPOSAL_APPROVED'
        : status === 'EXPIRED'
          ? 'TRIP_NO_LONGER_EDITABLE'
          : null,
    supersededByProposalId: status === 'SUPERSEDED' ? 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa' : null,
  };
}
