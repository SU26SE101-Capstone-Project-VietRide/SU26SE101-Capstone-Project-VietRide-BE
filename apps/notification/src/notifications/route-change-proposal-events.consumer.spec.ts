import {
  TRIP_ROUTE_CHANGE_PROPOSAL_APPROVED_ROUTING_KEY,
  TRIP_ROUTE_CHANGE_PROPOSAL_CREATED_ROUTING_KEY,
} from '@vietride/contracts';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import { NotificationType } from '../generated/notification-prisma-client';
import { MessageIdempotencyService } from './message-idempotency.service';
import { NotificationsService } from './notifications.service';
import type { OperatorRecipientProvider } from './operator-recipient.provider';
import { RouteChangeProposalEventsConsumer } from './route-change-proposal-events.consumer';
import { ROUTE_CHANGE_PROPOSAL_QUEUE_BINDINGS } from './route-change-proposal-events.constants';
import { TripAnnouncementRecipientProvider } from './trip-announcement-recipient.provider';

const EVENT_ID = '11111111-1111-4111-8111-111111111111';
const OPERATOR_ID = '22222222-2222-4222-8222-222222222222';
const PROPOSER_ID = '33333333-3333-4333-8333-333333333333';
const ADMIN_ID = '44444444-4444-4444-8444-444444444444';
const DRIVER_ID = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';

describe('RouteChangeProposalEventsConsumer', () => {
  let rabbit: jest.Mocked<RabbitMqConsumer>;
  let idempotency: jest.Mocked<MessageIdempotencyService>;
  let notifications: jest.Mocked<NotificationsService>;
  let recipients: jest.Mocked<OperatorRecipientProvider>;
  let tripRecipients: jest.Mocked<TripAnnouncementRecipientProvider>;
  let consumer: RouteChangeProposalEventsConsumer;

  beforeEach(() => {
    rabbit = { subscribe: jest.fn() } as unknown as jest.Mocked<RabbitMqConsumer>;
    idempotency = {
      begin: jest.fn(async () => 'acquired'),
      markProcessed: jest.fn(async () => undefined),
      release: jest.fn(async () => undefined),
    } as unknown as jest.Mocked<MessageIdempotencyService>;
    notifications = {
      createNotification: jest.fn(async () => ({})),
    } as unknown as jest.Mocked<NotificationsService>;
    recipients = {
      resolveOperatorRecipientUserIds: jest.fn(async () => []),
    } as unknown as jest.Mocked<OperatorRecipientProvider>;
    tripRecipients = {
      resolveTripCrewUserIds: jest.fn(async () => [DRIVER_ID]),
    } as unknown as jest.Mocked<TripAnnouncementRecipientProvider>;
    consumer = new RouteChangeProposalEventsConsumer(
      rabbit,
      idempotency,
      notifications,
      recipients,
      tripRecipients,
    );
  });

  it('binds five durable retry-enabled queue identities', async () => {
    await consumer.onModuleInit();

    expect(rabbit.subscribe).toHaveBeenCalledTimes(5);
    for (const binding of ROUTE_CHANGE_PROPOSAL_QUEUE_BINDINGS) {
      expect(rabbit.subscribe).toHaveBeenCalledWith(
        binding.queue,
        binding.routingKey,
        expect.any(Function),
        { prefetch: 1, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000 },
      );
    }
  });

  it('resolves CREATED recipients through Identity and skips a redelivery by eventId', async () => {
    idempotency.begin.mockResolvedValueOnce('acquired').mockResolvedValueOnce('duplicate');
    recipients.resolveOperatorRecipientUserIds.mockResolvedValue([ADMIN_ID, ADMIN_ID]);
    const payload = eventPayload('PENDING');

    await consumer.handle(
      TRIP_ROUTE_CHANGE_PROPOSAL_CREATED_ROUTING_KEY,
      payload,
      message('different-broker-id'),
    );
    await consumer.handle(
      TRIP_ROUTE_CHANGE_PROPOSAL_CREATED_ROUTING_KEY,
      payload,
      message('redelivery-broker-id'),
    );

    expect(idempotency.begin).toHaveBeenNthCalledWith(
      1,
      TRIP_ROUTE_CHANGE_PROPOSAL_CREATED_ROUTING_KEY,
      EVENT_ID,
      expect.any(Buffer),
    );
    expect(recipients.resolveOperatorRecipientUserIds).toHaveBeenCalledTimes(1);
    expect(recipients.resolveOperatorRecipientUserIds).toHaveBeenCalledWith(OPERATOR_ID);
    expect(notifications.createNotification).toHaveBeenCalledTimes(2);
    expect(notifications.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: ADMIN_ID,
        type: NotificationType.ROUTE_CHANGE_PROPOSAL_CREATED,
        dedupeKey: `${TRIP_ROUTE_CHANGE_PROPOSAL_CREATED_ROUTING_KEY}:${EVENT_ID}:${ADMIN_ID}:${NotificationType.ROUTE_CHANGE_PROPOSAL_CREATED}`,
      }),
    );
    expect(idempotency.markProcessed).toHaveBeenCalledWith(
      TRIP_ROUTE_CHANGE_PROPOSAL_CREATED_ROUTING_KEY,
      EVENT_ID,
    );
  });

  it('notifies current crew and proposedByUserId for terminal events', async () => {
    await consumer.handle(
      TRIP_ROUTE_CHANGE_PROPOSAL_APPROVED_ROUTING_KEY,
      eventPayload('APPROVED'),
      message(EVENT_ID),
    );

    expect(recipients.resolveOperatorRecipientUserIds).not.toHaveBeenCalled();
    expect(tripRecipients.resolveTripCrewUserIds).toHaveBeenCalledWith(
      '66666666-6666-4666-8666-666666666666',
      OPERATOR_ID,
    );
    expect(notifications.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({ userId: DRIVER_ID }),
    );
    expect(notifications.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: PROPOSER_ID,
        type: NotificationType.ROUTE_CHANGE_PROPOSAL_APPROVED,
      }),
    );
  });

  it('marks malformed deliveries processed by broker MessageId without side effects', async () => {
    await consumer.handle(
      TRIP_ROUTE_CHANGE_PROPOSAL_CREATED_ROUTING_KEY,
      { ...eventPayload('PENDING'), operatorId: 'invalid' },
      message('broker-message-id'),
    );

    expect(recipients.resolveOperatorRecipientUserIds).not.toHaveBeenCalled();
    expect(notifications.createNotification).not.toHaveBeenCalled();
    expect(idempotency.markProcessed).toHaveBeenCalledWith(
      TRIP_ROUTE_CHANGE_PROPOSAL_CREATED_ROUTING_KEY,
      'broker-message-id',
    );
  });
});

function message(messageId: string): ConsumeMessage {
  return {
    content: Buffer.from('{}'),
    properties: { messageId, correlationId: undefined },
  } as ConsumeMessage;
}

function eventPayload(status: 'PENDING' | 'APPROVED'): Record<string, unknown> {
  return {
    eventId: EVENT_ID,
    occurredAt: '2026-08-04T03:00:00Z',
    proposalId: '55555555-5555-4555-8555-555555555555',
    tripId: '66666666-6666-4666-8666-666666666666',
    operatorId: OPERATOR_ID,
    proposedByUserId: PROPOSER_ID,
    actorUserId: status === 'PENDING' ? PROPOSER_ID : '88888888-8888-4888-8888-888888888888',
    proposalType: 'CUSTOM',
    status,
    sourceAlternativeRouteId: null,
    approvedAlternativeRouteId:
      status === 'APPROVED' ? '77777777-7777-4777-8777-777777777777' : null,
    incidentId: null,
    reason: 'Road obstruction',
    rejectionReason: null,
    resolutionCode: null,
    supersededByProposalId: null,
  };
}
