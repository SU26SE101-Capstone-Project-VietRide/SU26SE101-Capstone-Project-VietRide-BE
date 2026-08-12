import type { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { RedisService } from '@vietride/nest-redis';
import type { ConsumeMessage } from 'amqplib';
import { RouteStateGenerationRegistry } from '../route-state/route-state-generation.registry';
import type { OffRouteService } from './off-route.service';
import { TripTerminalOffRouteConsumer } from './trip-terminal-off-route.consumer';

const EVENT_ID = '11111111-1111-4111-8111-111111111111';
const TRIP_ID = '22222222-2222-4222-8222-222222222222';
const OPERATOR_ID = '33333333-3333-4333-8333-333333333333';

describe('TripTerminalOffRouteConsumer', () => {
  it('subscribes to all terminal Trip facts with bounded retries', async () => {
    const fixture = createFixture();

    await fixture.consumer.onModuleInit();

    expect(fixture.subscribe.mock.calls.map((call) => call.slice(0, 2))).toEqual([
      ['tracking-off-route-trip-completed', 'trip.trip.completed'],
      ['tracking-off-route-trip-cancelled', 'trip.trip.cancelled'],
      ['tracking-off-route-trip-disrupted', 'trip.trip.disrupted'],
    ]);
    for (const call of fixture.subscribe.mock.calls) {
      expect(call[3]).toEqual({ prefetch: 1, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000 });
    }
  });

  it.each([
    ['trip.trip.completed', completedPayload()],
    ['trip.trip.cancelled', cancelledPayload()],
    ['trip.trip.disrupted', disruptedPayload()],
  ])('invalidates generation and clears off-route state for %s', async (routingKey, payload) => {
    const fixture = createFixture();

    await invoke(fixture, routingKey, payload);

    expect(fixture.routeStateGeneration.capture(TRIP_ID)).toBe(1);
    expect(fixture.clearRuntimeState).toHaveBeenCalledWith(TRIP_ID);
    expect(fixture.redisClient.eval).toHaveBeenCalledWith(
      expect.any(String),
      2,
      `tracking:off_route_terminal:processing:${EVENT_ID}`,
      `tracking:off_route_terminal:processed:${EVENT_ID}`,
      expect.any(String),
      604_800,
    );
  });

  it('skips a duplicate already marked processed', async () => {
    const fixture = createFixture();
    fixture.redisClient.get.mockResolvedValue('1');

    await invoke(fixture, 'trip.trip.completed', completedPayload());

    expect(fixture.clearRuntimeState).not.toHaveBeenCalled();
    expect(fixture.routeStateGeneration.capture(TRIP_ID)).toBe(0);
    expect(fixture.redisClient.set).not.toHaveBeenCalled();
  });

  it('invalidates before waiting for runtime cleanup so an in-flight GPS evaluation is fenced', async () => {
    const fixture = createFixture();
    let finishCleanup: (() => void) | undefined;
    let signalCleanupStarted: (() => void) | undefined;
    const cleanupStarted = new Promise<void>((resolve) => { signalCleanupStarted = resolve; });
    fixture.clearRuntimeState.mockImplementationOnce(
      () => {
        signalCleanupStarted?.();
        return new Promise<void>((resolve) => { finishCleanup = resolve; });
      },
    );

    const processing = invoke(fixture, 'trip.trip.completed', completedPayload());
    await cleanupStarted;

    expect(fixture.routeStateGeneration.capture(TRIP_ID)).toBe(1);
    finishCleanup?.();
    await expect(processing).resolves.toBeUndefined();
  });

  it('releases the event lock and retries when cleanup fails', async () => {
    const fixture = createFixture();
    fixture.clearRuntimeState.mockRejectedValueOnce(new Error('redis unavailable'));

    await expect(invoke(fixture, 'trip.trip.completed', completedPayload()))
      .rejects.toThrow('redis unavailable');

    expect(fixture.redisClient.eval).toHaveBeenCalledWith(
      expect.any(String),
      1,
      `tracking:off_route_terminal:processing:${EVENT_ID}`,
      expect.any(String),
    );
  });
});

function createFixture() {
  const handlers = new Map<string, (payload: unknown, raw: ConsumeMessage) => Promise<void>>();
  const subscribe = jest.fn(async (
    _queue: string,
    routingKey: string,
    handler: (payload: unknown, raw: ConsumeMessage) => Promise<void>,
    options: unknown,
  ) => {
    void options;
    handlers.set(routingKey, handler);
  });
  const redisClient = {
    get: jest.fn().mockResolvedValue(null),
    set: jest.fn().mockResolvedValue('OK'),
    eval: jest.fn().mockResolvedValue(1),
  };
  const clearRuntimeState = jest.fn().mockResolvedValue(undefined);
  const routeStateGeneration = new RouteStateGenerationRegistry();
  const consumer = new TripTerminalOffRouteConsumer(
    { subscribe } as unknown as RabbitMqConsumer,
    { getClient: () => redisClient } as unknown as RedisService,
    { clearRuntimeState } as unknown as OffRouteService,
    routeStateGeneration,
  );
  return {
    consumer,
    subscribe,
    handlers,
    redisClient,
    clearRuntimeState,
    routeStateGeneration,
  };
}

async function invoke(
  fixture: ReturnType<typeof createFixture>,
  routingKey: string,
  payload: unknown,
): Promise<void> {
  await fixture.consumer.onModuleInit();
  const handler = fixture.handlers.get(routingKey);
  if (!handler) throw new Error(`Missing handler for ${routingKey}`);
  await handler(payload, raw(payload));
}

function completedPayload() {
  return {
    eventId: EVENT_ID,
    occurredAt: '2026-08-12T01:00:00Z',
    tripId: TRIP_ID,
    operatorId: OPERATOR_ID,
    terminalAt: '2026-08-12T01:00:00Z',
    hasSubstitution: false,
  };
}

function cancelledPayload() {
  return {
    eventId: EVENT_ID,
    occurredAt: '2026-08-12T01:00:00Z',
    tripId: TRIP_ID,
    operatorId: OPERATOR_ID,
    cancelledAt: '2026-08-12T01:00:00Z',
    cancelReason: 'OPERATOR_CANCELLED',
  };
}

function disruptedPayload() {
  return { ...completedPayload(), reason: 'VEHICLE_BREAKDOWN' };
}

function raw(payload: unknown): ConsumeMessage {
  return {
    content: Buffer.from(JSON.stringify(payload)),
    fields: {} as ConsumeMessage['fields'],
    properties: { messageId: EVENT_ID } as ConsumeMessage['properties'],
  };
}
