import { BookingCreatedRealtimeConsumer } from './booking-created-realtime.consumer';
import type { LocationGateway } from './location.gateway';
import type { ConsumeMessage } from 'amqplib';

const EVENT_ID = '55555555-5555-4555-8555-555555555555';
const PROCESSING_KEY = `tracking:booking_created:processing:${EVENT_ID}`;
const PROCESSED_KEY = `tracking:booking_created:processed:${EVENT_ID}`;

describe('BookingCreatedRealtimeConsumer', () => {
  it('emits once for the same eventId even when broker MessageId changes', async () => {
    const { redisClient, values } = createRedisClient();
    const gateway = { emitBookingCreated: jest.fn() };
    const consumer = createConsumer(redisClient, gateway);
    const payload = createPayload();

    await consumer.handle(payload, createMessage('broker-message-1'));
    await consumer.handle(payload, createMessage('broker-message-2'));

    expect(gateway.emitBookingCreated).toHaveBeenCalledTimes(1);
    expect(values.get(PROCESSED_KEY)).toBe('1');
  });

  it('does not emit a duplicate processed message', async () => {
    const { redisClient } = createRedisClient({ [PROCESSED_KEY]: '1' });
    const gateway = { emitBookingCreated: jest.fn() };
    const consumer = createConsumer(redisClient, gateway);

    await consumer.handle(createPayload(), createMessage('broker-message'));

    expect(gateway.emitBookingCreated).not.toHaveBeenCalled();
    expect(redisClient.set).not.toHaveBeenCalled();
  });

  it('throws on a lock collision without deleting the foreign lock', async () => {
    const { redisClient, values } = createRedisClient({ [PROCESSING_KEY]: 'foreign-owner' });
    const gateway = { emitBookingCreated: jest.fn() };
    const consumer = createConsumer(redisClient, gateway);

    await expect(consumer.handle(createPayload(), createMessage('broker-message')))
      .rejects.toThrow(`MESSAGE_LOCKED_booking.booking.created_${EVENT_ID}`);

    expect(gateway.emitBookingCreated).not.toHaveBeenCalled();
    expect(values.get(PROCESSING_KEY)).toBe('foreign-owner');
    expect(redisClient.eval).not.toHaveBeenCalled();
  });

  it('marks a malformed payload processed and does not emit', async () => {
    const malformedMessageId = 'broker-malformed';
    const { redisClient, values } = createRedisClient();
    const gateway = { emitBookingCreated: jest.fn() };
    const consumer = createConsumer(redisClient, gateway);

    await consumer.handle({ ...createPayload(), status: 'PENDING' }, createMessage(malformedMessageId));

    expect(gateway.emitBookingCreated).not.toHaveBeenCalled();
    expect(values.get(`tracking:booking_created:processed:${malformedMessageId}`)).toBe('1');
  });

  it('releases its lock and does not mark processed when gateway emit fails', async () => {
    const { redisClient, values } = createRedisClient();
    const gateway = { emitBookingCreated: jest.fn(() => { throw new Error('gateway unavailable'); }) };
    const consumer = createConsumer(redisClient, gateway);

    await expect(consumer.handle(createPayload(), createMessage('broker-message')))
      .rejects.toThrow('gateway unavailable');

    expect(values.has(PROCESSING_KEY)).toBe(false);
    expect(values.has(PROCESSED_KEY)).toBe(false);
    expect(redisClient.eval).toHaveBeenCalledWith(
      expect.any(String),
      1,
      PROCESSING_KEY,
      expect.any(String),
    );
  });

  it('releases its lock when the processed marker write fails', async () => {
    const { redisClient, values } = createRedisClient();
    redisClient.set.mockImplementationOnce(async () => 'OK')
      .mockImplementationOnce(async () => { throw new Error('redis unavailable'); });
    const gateway = { emitBookingCreated: jest.fn() };
    const consumer = createConsumer(redisClient, gateway);

    await expect(consumer.handle(createPayload(), createMessage('broker-message')))
      .rejects.toThrow('redis unavailable');

    expect(gateway.emitBookingCreated).toHaveBeenCalledTimes(1);
    expect(values.has(PROCESSING_KEY)).toBe(false);
    expect(values.has(PROCESSED_KEY)).toBe(false);
    expect(redisClient.eval).toHaveBeenCalled();
  });

  it('keeps dead-letter retry subscription options', async () => {
    const subscribe = jest.fn(async () => undefined);
    const consumer = new BookingCreatedRealtimeConsumer(
      { subscribe } as never,
      { getClient: jest.fn() } as never,
      { emitBookingCreated: jest.fn() } as unknown as LocationGateway,
    );

    await consumer.onModuleInit();

    expect(subscribe).toHaveBeenCalledWith(
      'tracking:booking-created',
      'booking.booking.created',
      expect.any(Function),
      { prefetch: 1, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000 },
    );
  });
});

function createConsumer(
  redisClient: ReturnType<typeof createRedisClient>['redisClient'],
  gateway: { emitBookingCreated: jest.Mock },
): BookingCreatedRealtimeConsumer {
  return new BookingCreatedRealtimeConsumer(
    { subscribe: jest.fn() } as never,
    { getClient: () => redisClient } as never,
    gateway as unknown as LocationGateway,
  );
}

function createRedisClient(initial: Record<string, string> = {}) {
  const values = new Map(Object.entries(initial));
  const redisClient = {
    get: jest.fn(async (key: string) => values.get(key) ?? null),
    set: jest.fn(async (key: string, value: string, ...args: unknown[]) => {
      if (args.includes('NX') && values.has(key)) return null;
      values.set(key, value);
      return 'OK';
    }),
    eval: jest.fn(async (_script: string, _keyCount: number, key: string, ownerToken: string) => {
      if (values.get(key) !== ownerToken) return 0;
      values.delete(key);
      return 1;
    }),
  };
  return { redisClient, values };
}

function createMessage(messageId: string): ConsumeMessage {
  return {
    content: Buffer.from('{}'),
    properties: { messageId },
  } as ConsumeMessage;
}

function createPayload() {
  return {
    eventId: EVENT_ID,
    occurredAt: '2026-08-05T01:00:00.000Z',
    bookingId: '33333333-3333-4333-8333-333333333333',
    bookingCode: 'VR-20260805-ABCDEFGH',
    tripId: '44444444-4444-4444-8444-444444444444',
    status: 'CONFIRMED',
    ticketCodes: ['VT-20260805-ABCDEFGH'],
    passengerCount: 1,
    pickup: { stationId: '66666666-6666-4666-8666-666666666666', stopId: null, address: null },
    dropoff: { stationId: null, stopId: '77777777-7777-4777-8777-777777777777', address: null },
    driverUserId: '11111111-1111-4111-8111-111111111111',
    assistantUserId: '22222222-2222-4222-8222-222222222222',
  };
}
