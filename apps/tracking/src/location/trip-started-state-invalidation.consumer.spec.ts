import type { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { RedisService } from '@vietride/nest-redis';
import type { ConsumeMessage } from 'amqplib';
import type { RouteGeometryProvider } from '../off-route/route-geometry.provider';
import { RouteStateGenerationRegistry } from '../route-state/route-state-generation.registry';
import { TripStartedStateInvalidationConsumer } from './trip-started-state-invalidation.consumer';

const MESSAGE_ID = '11111111-1111-4111-8111-111111111111';
const TRIP_ID = '22222222-2222-4222-8222-222222222222';
const STOP_ID = '33333333-3333-4333-8333-333333333333';

describe('TripStartedStateInvalidationConsumer', () => {
  it('registers the canonical routing key with bounded dead-letter retry', async () => {
    const fixture = createFixture();

    await fixture.consumer.onModuleInit();

    expect(fixture.subscribe).toHaveBeenCalledWith(
      'tracking:trip-started-state-invalidation',
      'trip.trip.started',
      expect.any(Function),
      { prefetch: 1, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000 },
    );
  });

  it('invalidates route status and all pre-departure ETA state exactly once', async () => {
    const fixture = createFixture();
    fixture.redisClient.get.mockResolvedValue(null);
    fixture.redisClient.set.mockResolvedValue('OK');
    fixture.redisClient.scan.mockResolvedValueOnce([
      '0',
      [`tracking:eta:${TRIP_ID}:${STOP_ID}`],
    ]);
    fixture.redisClient.del.mockResolvedValue(1);
    fixture.redisClient.eval.mockResolvedValue(1);

    await invoke(fixture, validPayload());

    expect(fixture.routeGeometry.invalidateRouteGeometry).toHaveBeenCalledWith(TRIP_ID);
    expect(fixture.routeStateGeneration.capture(TRIP_ID)).toBe(1);
    expect(fixture.redisClient.del).toHaveBeenCalledWith(
      `tracking:eta_state:${TRIP_ID}`,
      `tracking:eta_batch_lock:${TRIP_ID}`,
    );
    expect(fixture.redisClient.del).toHaveBeenCalledWith(`tracking:eta:${TRIP_ID}:${STOP_ID}`);

    fixture.redisClient.get.mockResolvedValue('1');
    await invoke(fixture, validPayload());
    expect(fixture.routeStateGeneration.capture(TRIP_ID)).toBe(1);
  });

  it('drops malformed payloads without invalidating current state', async () => {
    const fixture = createFixture();
    fixture.redisClient.get.mockResolvedValue(null);
    fixture.redisClient.set.mockResolvedValue('OK');
    fixture.redisClient.eval.mockResolvedValue(1);

    await invoke(fixture, { tripId: 'invalid', actualDepartureTime: 'invalid' });

    expect(fixture.routeGeometry.invalidateRouteGeometry).not.toHaveBeenCalled();
    expect(fixture.routeStateGeneration.capture(TRIP_ID)).toBe(0);
  });
});

function createFixture() {
  let handler: ((payload: unknown, raw: ConsumeMessage) => Promise<void>) | undefined;
  const subscribe = jest.fn(async (
    _queue: string,
    _routingKey: string,
    registeredHandler: (payload: unknown, raw: ConsumeMessage) => Promise<void>,
  ) => {
    handler = registeredHandler;
  });
  const redisClient = {
    get: jest.fn(),
    set: jest.fn(),
    scan: jest.fn(),
    del: jest.fn(),
    eval: jest.fn(),
  };
  const routeGeometry = {
    peekCachedRouteGeometry: jest.fn(),
    getRouteGeometry: jest.fn(),
    invalidateRouteGeometry: jest.fn(),
  } as jest.Mocked<RouteGeometryProvider>;
  const routeStateGeneration = new RouteStateGenerationRegistry();
  const consumer = new TripStartedStateInvalidationConsumer(
    { subscribe } as unknown as RabbitMqConsumer,
    { getClient: () => redisClient } as unknown as RedisService,
    routeGeometry,
    routeStateGeneration,
  );
  return {
    consumer,
    subscribe,
    redisClient,
    routeGeometry,
    routeStateGeneration,
    getHandler: () => handler,
  };
}

async function invoke(fixture: ReturnType<typeof createFixture>, payload: unknown): Promise<void> {
  await fixture.consumer.onModuleInit();
  const handler = fixture.getHandler();
  if (!handler) throw new Error('consumer handler was not registered');
  await handler(payload, raw(payload));
}

function validPayload() {
  return {
    tripId: TRIP_ID,
    actualDepartureTime: '2026-08-11T03:00:00+00:00',
  };
}

function raw(payload: unknown): ConsumeMessage {
  return {
    content: Buffer.from(JSON.stringify(payload)),
    fields: {} as ConsumeMessage['fields'],
    properties: {
      messageId: MESSAGE_ID,
      correlationId: undefined,
    } as ConsumeMessage['properties'],
  };
}
