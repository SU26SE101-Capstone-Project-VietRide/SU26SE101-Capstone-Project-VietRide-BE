import { Injectable, OnModuleInit } from '@nestjs/common';
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
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import { LocationGateway } from './location.gateway';

const bindings = [
  [TRIP_ROUTE_CHANGE_PROPOSAL_CREATED_ROUTING_KEY, RouteChangeProposalCreatedEventSchema],
  [TRIP_ROUTE_CHANGE_PROPOSAL_APPROVED_ROUTING_KEY, RouteChangeProposalApprovedEventSchema],
  [TRIP_ROUTE_CHANGE_PROPOSAL_REJECTED_ROUTING_KEY, RouteChangeProposalRejectedEventSchema],
  [TRIP_ROUTE_CHANGE_PROPOSAL_SUPERSEDED_ROUTING_KEY, RouteChangeProposalSupersededEventSchema],
  [TRIP_ROUTE_CHANGE_PROPOSAL_EXPIRED_ROUTING_KEY, RouteChangeProposalExpiredEventSchema],
] as const;

@Injectable()
export class RouteChangeProposalRealtimeConsumer implements OnModuleInit {
  constructor(
    private readonly consumer: RabbitMqConsumer,
    private readonly gateway: LocationGateway,
  ) {}

  async onModuleInit(): Promise<void> {
    await Promise.all(bindings.map(([routingKey, schema]) => this.consumer.subscribe(
      `tracking.route-proposal.${routingKey.split('.').at(-1)}`,
      routingKey,
      async (payload) => {
        const parsed = schema.safeParse(payload);
        if (parsed.success) this.gateway.emitRouteProposal(parsed.data);
      },
      { prefetch: 10, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000 },
    )));
  }
}
