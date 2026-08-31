import type { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import { createHash } from 'node:crypto';
import type { TripShareGrantRepository } from './trip-share-grant.repository';
import type { TripShareMessageIdempotencyRepository } from './trip-share-message-idempotency.repository';
import type { TripShareRealtimePublisher } from './trip-share-realtime.publisher';
import { TripTerminalShareConsumer } from './trip-terminal-share.consumer';
import type { TripShareSubstitutionStateRepository } from './trip-share-substitution-state.repository';

const EVENT_ID = '11111111-1111-4111-8111-111111111111';
const BROKER_ID = 'broker-message-1';
const CORRELATION_ID = 'correlation-1';
const TRIP_ID = '22222222-2222-4222-8222-222222222222';
const OPERATOR_ID = '33333333-3333-4333-8333-333333333333';

describe('TripTerminalShareConsumer', () => {
  let subscribe: jest.Mock;
  let idempotency: jest.Mocked<TripShareMessageIdempotencyRepository>;
  let grants: jest.Mocked<TripShareGrantRepository>;
  let realtime: jest.Mocked<TripShareRealtimePublisher>;
  let consumer: TripTerminalShareConsumer;
  let substitutions: jest.Mocked<TripShareSubstitutionStateRepository>;

  beforeEach(() => {
    subscribe = jest.fn().mockResolvedValue(undefined);
    idempotency = {
      isProcessed: jest.fn().mockResolvedValue(false),
      acquire: jest.fn().mockResolvedValue('owner-token'),
      markProcessed: jest.fn().mockResolvedValue(true),
      release: jest.fn().mockResolvedValue(true),
    } as unknown as jest.Mocked<TripShareMessageIdempotencyRepository>;
    grants = {
      revokeAllActiveForTrip: jest.fn().mockResolvedValue(2),
      hasActiveForTrip: jest.fn().mockResolvedValue(true),
    } as unknown as jest.Mocked<TripShareGrantRepository>;
    realtime = {
      revokeTrip: jest.fn().mockResolvedValue(undefined),
    } as unknown as jest.Mocked<TripShareRealtimePublisher>;
    substitutions = {
      markPending: jest.fn().mockResolvedValue(undefined),
    } as unknown as jest.Mocked<TripShareSubstitutionStateRepository>;
    consumer = new TripTerminalShareConsumer(
      { subscribe } as unknown as RabbitMqConsumer,
      idempotency,
      grants,
      realtime,
      substitutions,
    );
  });

  it('registers three exact durable retry subscriptions', async () => {
    await consumer.onModuleInit();

    expect(subscribe).toHaveBeenCalledTimes(3);
    expect(subscribe.mock.calls.map((call) => [call[0], call[1], call[3]])).toEqual([
      ['tracking-trip-share-completed', 'trip.trip.completed', {
        prefetch: 1, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000,
      }],
      ['tracking-trip-share-cancelled', 'trip.trip.cancelled', {
        prefetch: 1, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000,
      }],
      ['tracking-trip-share-disrupted', 'trip.trip.disrupted', {
        prefetch: 1, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000,
      }],
    ]);
  });

  it.each([
    ['trip.trip.completed', completedPayload()],
    ['trip.trip.cancelled', cancelledPayload()],
    ['trip.trip.disrupted', disruptedPayload()],
  ])('validates and processes %s with DB -> realtime -> mark ordering', async (routingKey, payload) => {
    const order: string[] = [];
    grants.revokeAllActiveForTrip.mockImplementation(async () => { order.push('db'); return 2; });
    realtime.revokeTrip.mockImplementation(async () => { order.push('realtime'); });
    idempotency.markProcessed.mockImplementation(async () => { order.push('mark'); return true; });

    await invoke(consumer, subscribe, routingKey, payload, raw(payload, BROKER_ID));

    expect(idempotency.isProcessed).toHaveBeenCalledWith(EVENT_ID);
    expect(idempotency.acquire).toHaveBeenCalledWith(EVENT_ID);
    expect(grants.revokeAllActiveForTrip).toHaveBeenCalledWith(TRIP_ID, expect.any(Date));
    expect(realtime.revokeTrip).toHaveBeenCalledWith(TRIP_ID, 'TRIP_ENDED');
    expect(idempotency.markProcessed).toHaveBeenCalledWith(EVENT_ID, 'owner-token');
    expect(order).toEqual(['db', 'realtime', 'mark']);
  });

  it.each([
    ['trip.trip.completed', cancelledPayload()],
    ['trip.trip.cancelled', completedPayload()],
    ['trip.trip.disrupted', cancelledPayload()],
  ])('uses the routing-specific schema for %s', async (routingKey, wrongPayload) => {
    await invoke(consumer, subscribe, routingKey, wrongPayload, raw(wrongPayload, BROKER_ID));

    expect(idempotency.markProcessed).toHaveBeenCalledWith(EVENT_ID, 'owner-token');
    expect(grants.revokeAllActiveForTrip).not.toHaveBeenCalled();
    expect(realtime.revokeTrip).not.toHaveBeenCalled();
  });

  it('skips a duplicate before taking the processing lock', async () => {
    idempotency.isProcessed.mockResolvedValue(true);

    await invoke(consumer, subscribe, 'trip.trip.completed', completedPayload(), raw(completedPayload()));

    expect(idempotency.acquire).not.toHaveBeenCalled();
    expect(grants.revokeAllActiveForTrip).not.toHaveBeenCalled();
  });

  it('throws when the processing lock is busy so RabbitMQ retries', async () => {
    idempotency.acquire.mockResolvedValue(null);

    await expect(invoke(
      consumer,
      subscribe,
      'trip.trip.completed',
      completedPayload(),
      raw(completedPayload()),
    )).rejects.toThrow('TRIP_SHARE_TERMINAL_EVENT_LOCKED');
    expect(idempotency.markProcessed).not.toHaveBeenCalled();
  });

  it('skips after acquiring when a concurrent worker completed between the processed checks', async () => {
    idempotency.isProcessed.mockResolvedValueOnce(false).mockResolvedValueOnce(true);

    await invoke(consumer, subscribe, 'trip.trip.completed', completedPayload(), raw(completedPayload()));

    expect(idempotency.acquire).toHaveBeenCalledWith(EVENT_ID);
    expect(idempotency.release).toHaveBeenCalledWith(EVENT_ID, 'owner-token');
    expect(grants.revokeAllActiveForTrip).not.toHaveBeenCalled();
    expect(realtime.revokeTrip).not.toHaveBeenCalled();
    expect(idempotency.markProcessed).not.toHaveBeenCalled();
  });

  it('marks a malformed event processed and intentionally drops it', async () => {
    const malformed = { tripId: TRIP_ID, eventId: 'not-a-uuid', sensitive: 'do-not-store' };

    await invoke(consumer, subscribe, 'trip.trip.cancelled', malformed, raw(malformed, BROKER_ID));

    expect(idempotency.isProcessed).toHaveBeenCalledWith(BROKER_ID);
    expect(idempotency.markProcessed).toHaveBeenCalledWith(BROKER_ID, 'owner-token');
    expect(grants.revokeAllActiveForTrip).not.toHaveBeenCalled();
    expect(JSON.stringify([
      ...idempotency.isProcessed.mock.calls,
      ...idempotency.acquire.mock.calls,
      ...idempotency.markProcessed.mock.calls,
    ])).not.toContain('do-not-store');
  });

  it('deduplicates a malformed event by its valid payload eventId', async () => {
    const malformed = { eventId: EVENT_ID, tripId: 'not-a-uuid' };

    await invoke(
      consumer,
      subscribe,
      'trip.trip.completed',
      malformed,
      raw(malformed, 'republished-broker-id', 'republished-correlation-id'),
    );

    expect(idempotency.isProcessed).toHaveBeenCalledWith(EVENT_ID);
    expect(idempotency.markProcessed).toHaveBeenCalledWith(EVENT_ID, 'owner-token');
    expect(grants.revokeAllActiveForTrip).not.toHaveBeenCalled();
  });

  it.each([
    ['database', () => grants.revokeAllActiveForTrip.mockRejectedValue(new Error('db down'))],
    ['realtime', () => realtime.revokeTrip.mockRejectedValue(new Error('socket down'))],
  ])('releases the owned lock and rethrows a transient %s failure', async (_label, arrange) => {
    arrange();

    await expect(invoke(
      consumer,
      subscribe,
      'trip.trip.completed',
      completedPayload(),
      raw(completedPayload()),
    )).rejects.toThrow();

    expect(idempotency.release).toHaveBeenCalledWith(EVENT_ID, 'owner-token');
    expect(idempotency.markProcessed).not.toHaveBeenCalled();
  });

  it('always retries realtime and marks processed even when DB update count is zero', async () => {
    grants.revokeAllActiveForTrip.mockResolvedValue(0);

    await invoke(consumer, subscribe, 'trip.trip.completed', completedPayload(), raw(completedPayload()));

    expect(realtime.revokeTrip).toHaveBeenCalledWith(TRIP_ID, 'TRIP_ENDED');
    expect(idempotency.markProcessed).toHaveBeenCalledWith(EVENT_ID, 'owner-token');
  });

  it('preserves active grants and marks replacement pending for substituted disruption', async () => {
    const payload = { ...disruptedPayload(), hasSubstitution: true };

    await invoke(consumer, subscribe, 'trip.trip.disrupted', payload, raw(payload));

    expect(grants.revokeAllActiveForTrip).not.toHaveBeenCalled();
    expect(realtime.revokeTrip).not.toHaveBeenCalled();
    expect(substitutions.markPending).toHaveBeenCalledWith(TRIP_ID, payload.occurredAt);
    expect(idempotency.markProcessed).toHaveBeenCalledWith(EVENT_ID, 'owner-token');
  });

  it('does not create a stale pending marker after grants already moved', async () => {
    grants.hasActiveForTrip.mockResolvedValueOnce(false);
    const payload = { ...disruptedPayload(), hasSubstitution: true };

    await invoke(consumer, subscribe, 'trip.trip.disrupted', payload, raw(payload));

    expect(substitutions.markPending).not.toHaveBeenCalled();
    expect(grants.revokeAllActiveForTrip).not.toHaveBeenCalled();
  });

  it('releases and retries when Redis cannot complete the owned processing lock', async () => {
    idempotency.markProcessed.mockResolvedValue(false);

    await expect(invoke(
      consumer,
      subscribe,
      'trip.trip.completed',
      completedPayload(),
      raw(completedPayload()),
    )).rejects.toThrow('TRIP_SHARE_TERMINAL_EVENT_LOCK_NOT_OWNED');

    expect(idempotency.release).toHaveBeenCalledWith(EVENT_ID, 'owner-token');
  });

  it('prefers valid payload eventId over conflicting broker identifiers', async () => {
    await invoke(
      consumer,
      subscribe,
      'trip.trip.completed',
      completedPayload(),
      raw(completedPayload(), 'different-broker-id', CORRELATION_ID),
    );

    expect(idempotency.isProcessed).toHaveBeenCalledWith(EVENT_ID);
  });

  it('falls back from messageId to correlationId and finally raw-content SHA256', async () => {
    const malformed = { unexpected: true };
    await invoke(consumer, subscribe, 'trip.trip.completed', malformed, raw(malformed, BROKER_ID, CORRELATION_ID));
    expect(idempotency.isProcessed).toHaveBeenLastCalledWith(BROKER_ID);

    jest.clearAllMocks();
    idempotency.isProcessed.mockResolvedValue(false);
    idempotency.acquire.mockResolvedValue('owner-token');
    idempotency.markProcessed.mockResolvedValue(true);
    await invoke(consumer, subscribe, 'trip.trip.completed', malformed, raw(malformed, undefined, CORRELATION_ID));
    expect(idempotency.isProcessed).toHaveBeenLastCalledWith(CORRELATION_ID);

    jest.clearAllMocks();
    idempotency.isProcessed.mockResolvedValue(false);
    idempotency.acquire.mockResolvedValue('owner-token');
    idempotency.markProcessed.mockResolvedValue(true);
    const rawMessage = raw(malformed);
    await invoke(consumer, subscribe, 'trip.trip.completed', malformed, rawMessage);
    expect(idempotency.isProcessed).toHaveBeenLastCalledWith(
      createHash('sha256').update(rawMessage.content).digest('hex'),
    );
  });

  it('hashes unsafe or overlong broker identifiers before using them in Redis keys', async () => {
    const unsafe = `broker/id/${'x'.repeat(200)}`;
    const malformed = { unexpected: true };

    await invoke(consumer, subscribe, 'trip.trip.completed', malformed, raw(malformed, unsafe));

    expect(idempotency.isProcessed).toHaveBeenCalledWith(
      createHash('sha256').update(unsafe, 'utf8').digest('hex'),
    );
  });
});

async function invoke(
  consumer: TripTerminalShareConsumer,
  subscribe: jest.Mock,
  routingKey: string,
  payload: unknown,
  message: ConsumeMessage,
): Promise<void> {
  if (subscribe.mock.calls.length === 0) await consumer.onModuleInit();
  const call = subscribe.mock.calls.find((entry) => entry[1] === routingKey);
  if (!call) throw new Error(`Missing subscription for ${routingKey}`);
  await call[2](payload, message);
}

function completedPayload() {
  return {
    eventId: EVENT_ID,
    occurredAt: '2026-08-03T10:00:00.000Z',
    tripId: TRIP_ID,
    operatorId: OPERATOR_ID,
    terminalAt: '2026-08-03T10:00:00.000Z',
    hasSubstitution: false,
  };
}

function cancelledPayload() {
  return {
    eventId: EVENT_ID,
    occurredAt: '2026-08-03T10:00:00.000Z',
    tripId: TRIP_ID,
    operatorId: OPERATOR_ID,
    cancelledAt: '2026-08-03T10:00:00.000Z',
    cancelReason: 'OPERATOR_CANCELLED',
  };
}

function disruptedPayload() {
  return { ...completedPayload(), reason: 'VEHICLE_BREAKDOWN' };
}

function raw(
  payload: unknown,
  messageId?: string,
  correlationId?: string,
): ConsumeMessage {
  return {
    content: Buffer.from(JSON.stringify(payload)),
    properties: { messageId, correlationId, headers: {} },
  } as unknown as ConsumeMessage;
}
