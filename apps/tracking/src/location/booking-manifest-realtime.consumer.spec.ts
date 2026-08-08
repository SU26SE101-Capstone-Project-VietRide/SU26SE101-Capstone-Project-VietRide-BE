import type { ConsumeMessage } from 'amqplib';
import { BookingManifestRealtimeConsumer } from './booking-manifest-realtime.consumer';

const EVENT_ID = '11111111-1111-4111-8111-111111111111';

describe('BookingManifestRealtimeConsumer', () => {
  it('emits cancellation only once for a confirmed manifest booking', async () => {
    const { client } = createRedisClient();
    const gateway = createGateway();
    const consumer = createConsumer(client, gateway);
    const payload = cancelledPayload();
    await consumer.handleCancelled(payload, message());
    await consumer.handleCancelled(payload, message('replayed-broker-id'));
    expect(gateway.emitBookingCancelled).toHaveBeenCalledTimes(1);
  });

  it('skips non-manifest and terminal-trip cancellations', async () => {
    const { client } = createRedisClient();
    const gateway = createGateway();
    const consumer = createConsumer(client, gateway);
    await consumer.handleCancelled(cancelledPayload({ previousStatus: 'PENDING_PAYMENT' }), message());
    await consumer.handleCancelled(cancelledPayload({
      eventId: '22222222-2222-4222-8222-222222222222',
      cancellationReason: 'OPERATOR_CANCELLED_TRIP',
    }), message());
    expect(gateway.emitBookingCancelled).not.toHaveBeenCalled();
  });

  it('emits boarded and transferred operational facts', async () => {
    const { client } = createRedisClient();
    const gateway = createGateway();
    const consumer = createConsumer(client, gateway);
    await consumer.handleBoarded(boardedPayload(), message());
    await consumer.handleTransferred(transferredPayload(), message());
    expect(gateway.emitPassengerBoarded).toHaveBeenCalledTimes(1);
    expect(gateway.emitBookingTransferred).toHaveBeenCalledTimes(1);
  });

  it('releases its owned lock when socket emit fails', async () => {
    const { client, values } = createRedisClient();
    const gateway = createGateway();
    gateway.emitPassengerBoarded.mockImplementation(() => { throw new Error('socket unavailable'); });
    const consumer = createConsumer(client, gateway);
    await expect(consumer.handleBoarded(boardedPayload(), message())).rejects.toThrow('socket unavailable');
    expect(values.has(`tracking:booking_manifest:boarded:processed:${EVENT_ID}`)).toBe(false);
    expect(values.has(`tracking:booking_manifest:boarded:processing:${EVENT_ID}`)).toBe(false);
  });
});

function createConsumer(client: ReturnType<typeof createRedisClient>['client'], gateway: ReturnType<typeof createGateway>) {
  return new BookingManifestRealtimeConsumer(
    { subscribe: jest.fn() } as never,
    { getClient: () => client } as never,
    gateway as never,
  );
}

function createGateway() {
  return {
    emitBookingCancelled: jest.fn(),
    emitPassengerBoarded: jest.fn(),
    emitBookingTransferred: jest.fn(),
  };
}

function createRedisClient() {
  const values = new Map<string, string>();
  const client = {
    get: jest.fn(async (key: string) => values.get(key) ?? null),
    set: jest.fn(async (key: string, value: string, ...args: unknown[]) => {
      if (args.includes('NX') && values.has(key)) return null;
      values.set(key, value);
      return 'OK';
    }),
    eval: jest.fn(async (_script: string, _count: number, key: string, owner: string) => {
      if (values.get(key) !== owner) return 0;
      values.delete(key);
      return 1;
    }),
  };
  return { client, values };
}

function cancelledPayload(overrides: Record<string, unknown> = {}) {
  return {
    eventId: EVENT_ID,
    occurredAt: '2026-08-08T01:00:00.000Z',
    bookingId: '33333333-3333-4333-8333-333333333333',
    bookingCode: 'VR-20260808-ABCDEFGH',
    userId: '44444444-4444-4444-8444-444444444444',
    refundAmount: 100000,
    refundOverride: false,
    cancellationReason: 'USER_INITIATED',
    ticketCodes: ['VT-20260808-ABCDEFGH'],
    ticketCount: 1,
    tripId: '55555555-5555-4555-8555-555555555555',
    previousStatus: 'CONFIRMED',
    seatNumbers: ['A01'],
    ...overrides,
  };
}

function boardedPayload() {
  return {
    eventId: EVENT_ID,
    occurredAt: '2026-08-08T01:00:00.000Z',
    bookingId: '33333333-3333-4333-8333-333333333333',
    bookingCode: 'VR-20260808-ABCDEFGH',
    tripId: '55555555-5555-4555-8555-555555555555',
    passengerRecordId: '66666666-6666-4666-8666-666666666666',
    seatNumber: 'A01',
    ticketCode: 'VT-20260808-ABCDEFGH',
    boardedAt: '2026-08-08T01:00:00.000Z',
  };
}

function transferredPayload() {
  return {
    eventId: '77777777-7777-4777-8777-777777777777',
    occurredAt: '2026-08-08T01:00:00.000Z',
    sourceSubstitutionEventId: '88888888-8888-4888-8888-888888888888',
    bookingId: '33333333-3333-4333-8333-333333333333',
    recipientUserId: '44444444-4444-4444-8444-444444444444',
    operatorId: '99999999-9999-4999-8999-999999999999',
    oldTripId: '55555555-5555-4555-8555-555555555555',
    newTripId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
    newVehicleId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
    newVehiclePlateNumber: '51B-123.45',
    newTripDepartureDateTime: '2026-08-08T03:00:00.000Z',
    notifyPassengers: true,
    transfers: [{
      passengerId: '66666666-6666-4666-8666-666666666666',
      originalSeatNumber: 'A01',
      newSeatNumber: 'B01',
      confirmationStatus: 'NOT_REQUIRED',
    }],
  };
}

function message(messageId = 'broker-id'): ConsumeMessage {
  return { content: Buffer.from('{}'), properties: { messageId } } as ConsumeMessage;
}
