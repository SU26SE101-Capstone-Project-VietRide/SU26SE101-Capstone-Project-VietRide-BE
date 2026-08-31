import type { ConsumeMessage } from 'amqplib';
import { PARCEL_APPROVAL_REQUESTED_ROUTING_KEY } from '@vietride/contracts';
import { NotificationType } from '../generated/notification-prisma-client';
import { ParcelApprovalRequestedEventsConsumer } from './parcel-approval-requested-events.consumer';

const EVENT_ID = '11111111-1111-4111-8111-111111111111';
const REQUEST_ID = '22222222-2222-4222-8222-222222222222';
const DRIVER_ID = '33333333-3333-4333-8333-333333333333';
const OPERATOR_ID = '44444444-4444-4444-8444-444444444444';
const TRIP_ID = '55555555-5555-4555-8555-555555555555';
const STOP_ID = '66666666-6666-4666-8666-666666666666';

describe('ParcelApprovalRequestedEventsConsumer', () => {
  it('creates one deduplicated notification for the target Driver', async () => {
    const notifications = { createNotification: jest.fn(async () => ({})) };
    const idempotency = createIdempotency();
    const consumer = createConsumer(idempotency, notifications);

    await consumer.handle(createPayload(), createMessage());

    expect(notifications.createNotification).toHaveBeenCalledWith({
      userId: DRIVER_ID,
      type: NotificationType.PARCEL_APPROVAL_REQUESTED,
      title: 'Có yêu cầu Parcel cần phê duyệt',
      body: expect.any(String),
      data: expect.objectContaining({
        requestId: REQUEST_ID,
        requestType: 'STOP_DEPARTURE',
        tripId: TRIP_ID,
        stopId: STOP_ID,
      }),
      dedupeKey: `${PARCEL_APPROVAL_REQUESTED_ROUTING_KEY}:${EVENT_ID}:${DRIVER_ID}:${NotificationType.PARCEL_APPROVAL_REQUESTED}`,
    });
    expect(idempotency.markProcessed).toHaveBeenCalledWith(
      PARCEL_APPROVAL_REQUESTED_ROUTING_KEY,
      EVENT_ID,
    );
  });

  it('does not create a second notification for a processed redelivery', async () => {
    const notifications = { createNotification: jest.fn() };
    const idempotency = createIdempotency();
    idempotency.begin.mockResolvedValue('duplicate');
    const consumer = createConsumer(idempotency, notifications);

    await consumer.handle(createPayload(), createMessage());

    expect(notifications.createNotification).not.toHaveBeenCalled();
    expect(idempotency.markProcessed).not.toHaveBeenCalled();
  });

  it('marks malformed payloads processed without creating a notification', async () => {
    const notifications = { createNotification: jest.fn() };
    const idempotency = createIdempotency();
    const consumer = createConsumer(idempotency, notifications);

    await consumer.handle({ ...createPayload(), requestType: 'UNKNOWN' }, createMessage());

    expect(notifications.createNotification).not.toHaveBeenCalled();
    expect(idempotency.markProcessed).toHaveBeenCalledWith(
      PARCEL_APPROVAL_REQUESTED_ROUTING_KEY,
      EVENT_ID,
    );
  });
});

function createConsumer(
  idempotency: ReturnType<typeof createIdempotency>,
  notifications: { createNotification: jest.Mock },
) {
  return new ParcelApprovalRequestedEventsConsumer(
    { subscribe: jest.fn() } as never,
    idempotency as never,
    notifications as never,
  );
}

function createIdempotency() {
  return {
    begin: jest.fn<
      Promise<'acquired' | 'duplicate' | 'locked'>,
      [string, string, Buffer | undefined]
    >(async () => 'acquired'),
    markProcessed: jest.fn(async () => undefined),
    release: jest.fn(async () => undefined),
  };
}

function createPayload() {
  return {
    eventId: EVENT_ID,
    occurredAt: '2026-08-31T03:00:00.000Z',
    approvalRequestId: REQUEST_ID,
    requestType: 'STOP_DEPARTURE',
    operatorId: OPERATOR_ID,
    targetDriverUserId: DRIVER_ID,
    tripId: TRIP_ID,
    parcelId: null,
    incidentId: null,
    stopId: STOP_ID,
    expiresAt: null,
    validityCondition: 'WHILE_STOP_HAS_THE_SAME_UNRESOLVED_SNAPSHOT',
    actionType: 'OPEN_PARCEL_APPROVAL',
    actionParams: { requestId: REQUEST_ID, requestType: 'STOP_DEPARTURE' },
  };
}

function createMessage(): ConsumeMessage {
  return { content: Buffer.from('{}'), properties: { messageId: EVENT_ID } } as ConsumeMessage;
}
