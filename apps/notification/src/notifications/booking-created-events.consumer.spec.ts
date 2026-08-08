import type { ConsumeMessage } from 'amqplib';
import { NotificationType } from '../generated/notification-prisma-client';
import { BookingCreatedEventsConsumer } from './booking-created-events.consumer';
import type { MessageIdempotencyService } from './message-idempotency.service';
import type { NotificationsService } from './notifications.service';

const ROUTING_KEY = 'booking.booking.created';
const DRIVER_ID = '11111111-1111-4111-8111-111111111111';
const ASSISTANT_ID = '22222222-2222-4222-8222-222222222222';
const BOOKING_ID = '33333333-3333-4333-8333-333333333333';
const TRIP_ID = '44444444-4444-4444-8444-444444444444';
const EVENT_ID = '55555555-5555-4555-8555-555555555555';

describe('BookingCreatedEventsConsumer', () => {
  it('fans out one idempotent notification to each assigned crew member', async () => {
    const notifications = { createNotification: jest.fn(async () => ({})) };
    const idempotency = createIdempotency('acquired');
    const consumer = createConsumer(idempotency, notifications);

    await consumer.handle(createPayload(), createMessage('broker-message'));

    expect(notifications.createNotification).toHaveBeenCalledTimes(2);
    expect(notifications.createNotification).toHaveBeenCalledWith(expect.objectContaining({
      userId: DRIVER_ID,
      type: NotificationType.BOOKING_CREATED,
      dedupeKey: `${ROUTING_KEY}:${EVENT_ID}:${DRIVER_ID}`,
    }));
    expect(notifications.createNotification).toHaveBeenCalledWith(expect.objectContaining({
      userId: ASSISTANT_ID,
      type: NotificationType.BOOKING_CREATED,
      dedupeKey: `${ROUTING_KEY}:${EVENT_ID}:${ASSISTANT_ID}`,
    }));
    expect(idempotency.markProcessed).toHaveBeenCalledWith(ROUTING_KEY, EVENT_ID);
  });

  it('deduplicates a replay by eventId even when broker MessageId changes', async () => {
    const notifications = { createNotification: jest.fn(async () => ({})) };
    const idempotency = createIdempotency('acquired');
    (idempotency.begin as jest.Mock)
      .mockResolvedValueOnce('acquired')
      .mockResolvedValueOnce('duplicate');
    const consumer = createConsumer(idempotency, notifications);
    const payload = createPayload();

    await consumer.handle(payload, createMessage('broker-message-1'));
    await consumer.handle(payload, createMessage('broker-message-2'));

    expect(idempotency.begin).toHaveBeenNthCalledWith(1, ROUTING_KEY, EVENT_ID, expect.any(Buffer));
    expect(idempotency.begin).toHaveBeenNthCalledWith(2, ROUTING_KEY, EVENT_ID, expect.any(Buffer));
    expect(notifications.createNotification).toHaveBeenCalledTimes(2);
  });

  it('creates only one notification when driver and assistant are the same user', async () => {
    const notifications = { createNotification: jest.fn(async () => ({})) };
    const idempotency = createIdempotency('acquired');
    const consumer = createConsumer(idempotency, notifications);

    await consumer.handle(
      createPayload({ assistantUserId: DRIVER_ID }),
      createMessage('broker-message'),
    );

    expect(notifications.createNotification).toHaveBeenCalledTimes(1);
    expect(notifications.createNotification).toHaveBeenCalledWith(expect.objectContaining({
      userId: DRIVER_ID,
      dedupeKey: `${ROUTING_KEY}:${EVENT_ID}:${DRIVER_ID}`,
    }));
  });

  it('skips a duplicate message before parsing or writing notifications', async () => {
    const notifications = { createNotification: jest.fn() };
    const idempotency = createIdempotency('duplicate');
    const consumer = createConsumer(idempotency, notifications);

    await consumer.handle(createPayload(), createMessage('broker-message'));

    expect(notifications.createNotification).not.toHaveBeenCalled();
    expect(idempotency.markProcessed).not.toHaveBeenCalled();
  });

  it('marks a malformed payload processed without writing notifications', async () => {
    const notifications = { createNotification: jest.fn() };
    const idempotency = createIdempotency('acquired');
    const consumer = createConsumer(idempotency, notifications);

    await consumer.handle(
      { ...createPayload(), status: 'PENDING' },
      createMessage('broker-malformed'),
    );

    expect(notifications.createNotification).not.toHaveBeenCalled();
    expect(idempotency.markProcessed).toHaveBeenCalledWith(ROUTING_KEY, 'broker-malformed');
    expect(idempotency.release).not.toHaveBeenCalled();
  });

  it('releases the idempotency lock and rethrows transient notification failures', async () => {
    const notifications = {
      createNotification: jest.fn(async () => { throw new Error('notification unavailable'); }),
    };
    const idempotency = createIdempotency('acquired');
    const consumer = createConsumer(idempotency, notifications);

    await expect(consumer.handle(createPayload(), createMessage('broker-message')))
      .rejects.toThrow('notification unavailable');

    expect(idempotency.markProcessed).not.toHaveBeenCalled();
    expect(idempotency.release).toHaveBeenCalledWith(ROUTING_KEY, EVENT_ID);
  });

  it('keeps dead-letter retry subscription options', async () => {
    const subscribe = jest.fn(async () => undefined);
    const consumer = new BookingCreatedEventsConsumer(
      { subscribe } as never,
      createIdempotency('acquired') as unknown as MessageIdempotencyService,
      { createNotification: jest.fn() } as unknown as NotificationsService,
    );

    await consumer.onModuleInit();

    expect(subscribe).toHaveBeenCalledWith(
      'notification:booking-created',
      ROUTING_KEY,
      expect.any(Function),
      { prefetch: 1, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000 },
    );
  });
});

function createConsumer(
  idempotency: ReturnType<typeof createIdempotency>,
  notifications: { createNotification: jest.Mock },
): BookingCreatedEventsConsumer {
  return new BookingCreatedEventsConsumer(
    { subscribe: jest.fn() } as never,
    idempotency as unknown as MessageIdempotencyService,
    notifications as unknown as NotificationsService,
  );
}

function createIdempotency(state: 'acquired' | 'duplicate' | 'locked') {
  return {
    begin: jest.fn(async () => state),
    markProcessed: jest.fn(async () => undefined),
    release: jest.fn(async () => undefined),
  };
}

function createMessage(messageId: string): ConsumeMessage {
  return {
    content: Buffer.from('{}'),
    properties: { messageId },
  } as ConsumeMessage;
}

function createPayload(overrides: { driverUserId?: string; assistantUserId?: string } = {}) {
  return {
    eventId: EVENT_ID,
    occurredAt: '2026-08-05T01:00:00.000Z',
    bookingId: BOOKING_ID,
    bookingCode: 'VR-20260805-ABCDEFGH',
    tripId: TRIP_ID,
    status: 'CONFIRMED',
    ticketCodes: ['VT-20260805-ABCDEFGH'],
    seatNumbers: ['A01'],
    departureDateTime: '2026-08-05T03:00:00.000Z',
    passengerCount: 1,
    pickup: { stationId: '66666666-6666-4666-8666-666666666666', stopId: null, address: null },
    dropoff: { stationId: null, stopId: '77777777-7777-4777-8777-777777777777', address: null },
    driverUserId: overrides.driverUserId ?? DRIVER_ID,
    assistantUserId: overrides.assistantUserId ?? ASSISTANT_ID,
  };
}
