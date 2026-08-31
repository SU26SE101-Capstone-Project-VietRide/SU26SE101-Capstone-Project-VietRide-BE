import {
  RouteChangeProposalApprovedEventSchema,
  RouteChangeProposalCreatedEventSchema,
  RouteChangeProposalExpiredEventSchema,
  RouteChangeProposalRejectedEventSchema,
  RouteChangeProposalSupersededEventSchema,
  type RouteChangeProposalEvent,
  type RouteChangeProposalRoutingKey,
  TRIP_ROUTE_CHANGE_PROPOSAL_APPROVED_ROUTING_KEY,
  TRIP_ROUTE_CHANGE_PROPOSAL_CREATED_ROUTING_KEY,
  TRIP_ROUTE_CHANGE_PROPOSAL_EXPIRED_ROUTING_KEY,
  TRIP_ROUTE_CHANGE_PROPOSAL_REJECTED_ROUTING_KEY,
  TRIP_ROUTE_CHANGE_PROPOSAL_SUPERSEDED_ROUTING_KEY,
} from '@vietride/contracts';
import { z } from 'zod';
import { NotificationType } from '../generated/notification-prisma-client';
import type { CreateNotificationDto } from './dto/create-notification.dto';
import { formatDisplayReason } from './notification-display';

type ParseResult =
  | { success: true; data: RouteChangeProposalEvent }
  | { success: false; error: z.ZodError };

const schemaByRoutingKey: Record<
  RouteChangeProposalRoutingKey,
  z.ZodType<RouteChangeProposalEvent>
> = {
  [TRIP_ROUTE_CHANGE_PROPOSAL_CREATED_ROUTING_KEY]: RouteChangeProposalCreatedEventSchema,
  [TRIP_ROUTE_CHANGE_PROPOSAL_APPROVED_ROUTING_KEY]: RouteChangeProposalApprovedEventSchema,
  [TRIP_ROUTE_CHANGE_PROPOSAL_REJECTED_ROUTING_KEY]: RouteChangeProposalRejectedEventSchema,
  [TRIP_ROUTE_CHANGE_PROPOSAL_SUPERSEDED_ROUTING_KEY]: RouteChangeProposalSupersededEventSchema,
  [TRIP_ROUTE_CHANGE_PROPOSAL_EXPIRED_ROUTING_KEY]: RouteChangeProposalExpiredEventSchema,
};

const notificationTypeByRoutingKey: Record<RouteChangeProposalRoutingKey, NotificationType> = {
  [TRIP_ROUTE_CHANGE_PROPOSAL_CREATED_ROUTING_KEY]: NotificationType.ROUTE_CHANGE_PROPOSAL_CREATED,
  [TRIP_ROUTE_CHANGE_PROPOSAL_APPROVED_ROUTING_KEY]:
    NotificationType.ROUTE_CHANGE_PROPOSAL_APPROVED,
  [TRIP_ROUTE_CHANGE_PROPOSAL_REJECTED_ROUTING_KEY]:
    NotificationType.ROUTE_CHANGE_PROPOSAL_REJECTED,
  [TRIP_ROUTE_CHANGE_PROPOSAL_SUPERSEDED_ROUTING_KEY]:
    NotificationType.ROUTE_CHANGE_PROPOSAL_SUPERSEDED,
  [TRIP_ROUTE_CHANGE_PROPOSAL_EXPIRED_ROUTING_KEY]: NotificationType.ROUTE_CHANGE_PROPOSAL_EXPIRED,
};

const contentByRoutingKey: Record<
  RouteChangeProposalRoutingKey,
  { title: string; body: (event: RouteChangeProposalEvent) => string }
> = {
  [TRIP_ROUTE_CHANGE_PROPOSAL_CREATED_ROUTING_KEY]: {
    title: 'Có đề xuất đổi lộ trình mới',
    body: (event) => `Chuyến xe có đề xuất đổi lộ trình: ${formatDisplayReason(event.reason)}.`,
  },
  [TRIP_ROUTE_CHANGE_PROPOSAL_APPROVED_ROUTING_KEY]: {
    title: 'Đề xuất đổi lộ trình đã được duyệt',
    body: () => 'Đề xuất đổi lộ trình cho chuyến xe đã được duyệt.',
  },
  [TRIP_ROUTE_CHANGE_PROPOSAL_REJECTED_ROUTING_KEY]: {
    title: 'Đề xuất đổi lộ trình đã bị từ chối',
    body: (event) =>
      `Đề xuất đổi lộ trình cho chuyến xe đã bị từ chối${event.rejectionReason ? `: ${formatDisplayReason(event.rejectionReason)}` : '.'}`,
  },
  [TRIP_ROUTE_CHANGE_PROPOSAL_SUPERSEDED_ROUTING_KEY]: {
    title: 'Đề xuất đổi lộ trình đã được thay thế',
    body: () => 'Đề xuất đổi lộ trình cho chuyến xe đã được thay thế.',
  },
  [TRIP_ROUTE_CHANGE_PROPOSAL_EXPIRED_ROUTING_KEY]: {
    title: 'Đề xuất đổi lộ trình đã hết hiệu lực',
    body: () => 'Đề xuất đổi lộ trình cho chuyến xe đã hết hiệu lực.',
  },
};

export function parseRouteChangeProposalEvent(
  routingKey: RouteChangeProposalRoutingKey,
  payload: unknown,
): ParseResult {
  return schemaByRoutingKey[routingKey].safeParse(payload);
}

export function mapRouteChangeProposalToNotifications(
  routingKey: RouteChangeProposalRoutingKey,
  event: RouteChangeProposalEvent,
  resolvedRecipientUserIds: string[] = [],
): CreateNotificationDto[] {
  const recipientUserIds = routingKey === TRIP_ROUTE_CHANGE_PROPOSAL_CREATED_ROUTING_KEY
    ? [...resolvedRecipientUserIds, event.proposedByUserId]
    : [event.proposedByUserId];
  const content = contentByRoutingKey[routingKey];

  return [...new Set(recipientUserIds)].map((userId) => {
    const isCreatedConfirmation = routingKey === TRIP_ROUTE_CHANGE_PROPOSAL_CREATED_ROUTING_KEY
      && userId === event.proposedByUserId;
    return {
      userId,
      type: notificationTypeByRoutingKey[routingKey],
      title: isCreatedConfirmation ? 'Đã gửi đề xuất đổi lộ trình' : content.title,
      body: isCreatedConfirmation
        ? 'Đề xuất đổi lộ trình cho chuyến xe đã được gửi thành công.'
        : content.body(event),
      data: {
        eventId: event.eventId,
        occurredAt: event.occurredAt,
        proposalId: event.proposalId,
        tripId: event.tripId,
        operatorId: event.operatorId,
        proposedByUserId: event.proposedByUserId,
        actorUserId: event.actorUserId,
        proposalType: event.proposalType,
        status: event.status,
        sourceAlternativeRouteId: event.sourceAlternativeRouteId,
        approvedAlternativeRouteId: event.approvedAlternativeRouteId,
        incidentId: event.incidentId,
        reason: event.reason,
        rejectionReason: event.rejectionReason,
        resolutionCode: event.resolutionCode,
        supersededByProposalId: event.supersededByProposalId,
      },
    };
  });
}
