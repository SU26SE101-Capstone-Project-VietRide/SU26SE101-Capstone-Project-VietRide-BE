import type { ConsumeMessage } from 'amqplib';
import { NotificationType } from '../generated/notification-prisma-client';
import { PARCEL_RESERVED_ROUTING_KEY } from '@vietride/contracts';
import { ParcelReservedAssistantEventsConsumer } from './parcel-reserved-assistant-events.consumer';

const EVENT_ID = '11111111-1111-4111-8111-111111111111';
const PARCEL_ID = '22222222-2222-4222-8222-222222222222';
const TRIP_ID = '33333333-3333-4333-8333-333333333333';
const OPERATOR_ID = '44444444-4444-4444-8444-444444444444';
const ASSISTANT_ID = '55555555-5555-4555-8555-555555555555';

describe('ParcelReservedAssistantEventsConsumer', () => {
  it('creates one inbox and FCM-backed notification only for the assigned Assistant', async () => {
    const notifications = { createNotification: jest.fn(async () => ({})) };
    const idempotency = createIdempotency();
    const tripRecipients = {
      resolveTripAssistantUserId: jest.fn(async () => ASSISTANT_ID),
    };
    const consumer = createConsumer(idempotency, notifications, tripRecipients);

    await consumer.handle(createPayload(), createMessage());

    expect(tripRecipients.resolveTripAssistantUserId).toHaveBeenCalledWith(TRIP_ID, OPERATOR_ID);
    expect(notifications.createNotification).toHaveBeenCalledWith({
      userId: ASSISTANT_ID,
      type: NotificationType.PARCEL_RESERVED,
      title: 'Có đơn hàng mới cần xác nhận lên xe',
      body: 'Đơn hàng VRP-20260813-ABCDEFGH đã thanh toán cọc và được giữ chỗ trên chuyến.',
      data: expect.objectContaining({ parcelId: PARCEL_ID, tripId: TRIP_ID }),
      dedupeKey: `${PARCEL_RESERVED_ROUTING_KEY}:${EVENT_ID}:${ASSISTANT_ID}:${NotificationType.PARCEL_RESERVED}`,
    });
    expect(idempotency.markProcessed).toHaveBeenCalledWith(PARCEL_RESERVED_ROUTING_KEY, EVENT_ID);
  });

  it('marks an event with no assigned Assistant processed without creating a notification', async () => {
    const notifications = { createNotification: jest.fn() };
    const idempotency = createIdempotency();
    const consumer = createConsumer(idempotency, notifications, {
      resolveTripAssistantUserId: jest.fn(async () => null),
    });

    await consumer.handle(createPayload(), createMessage());

    expect(notifications.createNotification).not.toHaveBeenCalled();
    expect(idempotency.markProcessed).toHaveBeenCalledWith(PARCEL_RESERVED_ROUTING_KEY, EVENT_ID);
  });

  it('skips a redelivery that was already processed', async () => {
    const notifications = { createNotification: jest.fn() };
    const idempotency = createIdempotency();
    idempotency.begin.mockResolvedValue('duplicate');
    const tripRecipients = { resolveTripAssistantUserId: jest.fn() };
    const consumer = createConsumer(idempotency, notifications, tripRecipients);

    await consumer.handle(createPayload(), createMessage());

    expect(tripRecipients.resolveTripAssistantUserId).not.toHaveBeenCalled();
    expect(notifications.createNotification).not.toHaveBeenCalled();
    expect(idempotency.markProcessed).not.toHaveBeenCalled();
  });

  it('drops a malformed payload intentionally', async () => {
    const notifications = { createNotification: jest.fn() };
    const idempotency = createIdempotency();
    const tripRecipients = { resolveTripAssistantUserId: jest.fn() };
    const consumer = createConsumer(idempotency, notifications, tripRecipients);

    await consumer.handle({ ...createPayload(), parcelId: 'invalid' }, createMessage());

    expect(tripRecipients.resolveTripAssistantUserId).not.toHaveBeenCalled();
    expect(notifications.createNotification).not.toHaveBeenCalled();
    expect(idempotency.markProcessed).toHaveBeenCalledWith(PARCEL_RESERVED_ROUTING_KEY, EVENT_ID);
  });

  it('releases the processing lock when Trip lookup fails so RabbitMQ retries', async () => {
    const idempotency = createIdempotency();
    const consumer = createConsumer(idempotency, { createNotification: jest.fn() }, {
      resolveTripAssistantUserId: jest.fn(async () => { throw new Error('trip unavailable'); }),
    });

    await expect(consumer.handle(createPayload(), createMessage())).rejects.toThrow('trip unavailable');
    expect(idempotency.release).toHaveBeenCalledWith(PARCEL_RESERVED_ROUTING_KEY, EVENT_ID);
    expect(idempotency.markProcessed).not.toHaveBeenCalled();
  });
});

function createConsumer(
  idempotency: ReturnType<typeof createIdempotency>,
  notifications: { createNotification: jest.Mock },
  tripRecipients: { resolveTripAssistantUserId: jest.Mock },
) {
  return new ParcelReservedAssistantEventsConsumer(
    { subscribe: jest.fn() } as never,
    idempotency as never,
    notifications as never,
    tripRecipients as never,
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
    occurredAt: '2026-08-13T03:00:00.000Z',
    parcelId: PARCEL_ID,
    parcelCode: 'VRP-20260813-ABCDEFGH',
    tripId: TRIP_ID,
    operatorId: OPERATOR_ID,
    senderUserId: '66666666-6666-4666-8666-666666666666',
  };
}

function createMessage(): ConsumeMessage {
  return { content: Buffer.from('{}'), properties: { messageId: EVENT_ID } } as ConsumeMessage;
}
