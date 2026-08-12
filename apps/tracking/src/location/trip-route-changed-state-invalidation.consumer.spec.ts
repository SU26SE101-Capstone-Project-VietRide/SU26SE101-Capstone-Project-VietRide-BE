import type { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { RedisService } from '@vietride/nest-redis';
import type { ConsumeMessage } from 'amqplib';
import type { TripDataProvider } from '../eta/trip-data.provider';
import type { RouteGeometryProvider } from '../off-route/route-geometry.provider';
import type { OffRouteService } from '../off-route/off-route.service';
import { trackingTripDelayStateKey } from '../trip-delay/trip-delay.constants';
import { RouteStateGenerationRegistry } from '../route-state/route-state-generation.registry';
import { TripRouteChangedStateInvalidationConsumer } from './trip-route-changed-state-invalidation.consumer';

const EVENT_ID = '11111111-1111-4111-8111-111111111111';
const TRIP_ID = '22222222-2222-4222-8222-222222222222';
const STOP_ID = '66666666-6666-4666-8666-666666666666';
const STATION_ID = '77777777-7777-4777-8777-777777777777';
const PROCESSED_KEY = `tracking:trip_route_changed:processed:${EVENT_ID}`;
const PROCESSING_KEY = `tracking:trip_route_changed:processing:${EVENT_ID}`;

describe('TripRouteChangedStateInvalidationConsumer', () => {
  it('registers the canonical routing key with bounded dead-letter retry', async () => {
    const fixture = createFixture();

    await fixture.consumer.onModuleInit();

    expect(fixture.subscribe).toHaveBeenCalledWith(
      'tracking:trip-route-changed-state-invalidation',
      'trip.trip.route_changed',
      expect.any(Function),
      { prefetch: 1, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000 },
    );
  });

  it('invalidates provider caches and all Redis route-dependent state for a valid event', async () => {
    const fixture = createFixture();
    fixture.redisClient.get.mockResolvedValue(null);
    fixture.redisClient.set.mockResolvedValue('OK');
    fixture.redisClient.scan
      .mockResolvedValueOnce(['17', [`tracking:eta:${TRIP_ID}:${STOP_ID}`]])
      .mockResolvedValueOnce(['0', [`tracking:eta:${TRIP_ID}:${STATION_ID}`]])
      .mockResolvedValueOnce(['0', [trackingTripDelayStateKey(TRIP_ID, STOP_ID)]]);
    fixture.redisClient.del.mockResolvedValue(1);
    fixture.redisClient.eval.mockResolvedValue(1);

    await invoke(fixture, validPayload());

    expect(fixture.tripData.invalidateRouteStops).toHaveBeenCalledWith(TRIP_ID);
    expect(fixture.routeGeometry.invalidateRouteGeometry).toHaveBeenCalledWith(TRIP_ID);
    expect(fixture.routeStateGeneration.capture(TRIP_ID)).toBe(1);
    expect(fixture.offRoute.clearRuntimeState).toHaveBeenCalledWith(TRIP_ID);
    expect(fixture.redisClient.del).toHaveBeenCalledWith(
      `tracking:eta_state:${TRIP_ID}`,
      trackingTripDelayStateKey(TRIP_ID),
    );
    expect(fixture.redisClient.del).toHaveBeenCalledWith(`tracking:eta:${TRIP_ID}:${STOP_ID}`);
    expect(fixture.redisClient.del).toHaveBeenCalledWith(`tracking:eta:${TRIP_ID}:${STATION_ID}`);
    expect(fixture.redisClient.del).toHaveBeenCalledWith(
      trackingTripDelayStateKey(TRIP_ID, STOP_ID),
    );
    expect(fixture.redisClient.eval).toHaveBeenCalledWith(
      expect.any(String),
      2,
      PROCESSING_KEY,
      PROCESSED_KEY,
      expect.any(String),
      86_400,
    );
  });

  it('skips an already processed duplicate before acquiring a lock or invalidating state', async () => {
    const fixture = createFixture();
    fixture.redisClient.get.mockResolvedValue('1');

    await invoke(fixture, validPayload());

    expect(fixture.redisClient.set).not.toHaveBeenCalled();
    expect(fixture.tripData.invalidateRouteStops).not.toHaveBeenCalled();
    expect(fixture.routeGeometry.invalidateRouteGeometry).not.toHaveBeenCalled();
    expect(fixture.routeStateGeneration.capture(TRIP_ID)).toBe(0);
    expect(fixture.redisClient.del).not.toHaveBeenCalled();
  });

  it('marks a malformed payload processed and intentionally drops it', async () => {
    const fixture = createFixture();
    fixture.redisClient.get.mockResolvedValue(null);
    fixture.redisClient.set.mockResolvedValue('OK');
    fixture.redisClient.eval.mockResolvedValue(1);

    await invoke(fixture, { eventId: EVENT_ID, tripId: 'not-a-uuid' });

    expect(fixture.tripData.invalidateRouteStops).not.toHaveBeenCalled();
    expect(fixture.routeGeometry.invalidateRouteGeometry).not.toHaveBeenCalled();
    expect(fixture.routeStateGeneration.capture(TRIP_ID)).toBe(0);
    expect(fixture.redisClient.eval).toHaveBeenCalledWith(
      expect.any(String),
      2,
      PROCESSING_KEY,
      PROCESSED_KEY,
      expect.any(String),
      86_400,
    );
  });

  it('releases only its owned lock and throws when invalidation fails transiently', async () => {
    const fixture = createFixture();
    fixture.redisClient.get.mockResolvedValue(null);
    fixture.redisClient.set.mockResolvedValue('OK');
    fixture.redisClient.del.mockRejectedValueOnce(new Error('redis unavailable'));
    fixture.redisClient.eval.mockResolvedValue(1);

    await expect(invoke(fixture, validPayload())).rejects.toThrow('redis unavailable');

    expect(fixture.redisClient.eval).toHaveBeenCalledTimes(1);
    expect(fixture.redisClient.eval).toHaveBeenCalledWith(
      expect.any(String),
      1,
      PROCESSING_KEY,
      expect.any(String),
    );
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
  const tripData = {
    getRouteStops: jest.fn(),
    invalidateRouteStops: jest.fn(),
  } as jest.Mocked<TripDataProvider>;
  const routeGeometry = {
    peekCachedRouteGeometry: jest.fn(),
    getRouteGeometry: jest.fn(),
    invalidateRouteGeometry: jest.fn(),
  } as jest.Mocked<RouteGeometryProvider>;
  const routeStateGeneration = new RouteStateGenerationRegistry();
  const offRoute = {
    clearRuntimeState: jest.fn(),
  } as unknown as jest.Mocked<OffRouteService>;
  const consumer = new TripRouteChangedStateInvalidationConsumer(
    { subscribe } as unknown as RabbitMqConsumer,
    { getClient: () => redisClient } as unknown as RedisService,
    tripData,
    routeGeometry,
    routeStateGeneration,
    offRoute,
  );

  return {
    consumer,
    subscribe,
    redisClient,
    tripData,
    routeGeometry,
    routeStateGeneration,
    offRoute,
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
    eventId: EVENT_ID,
    occurredAt: '2026-08-10T01:00:00Z',
    tripId: TRIP_ID,
    operatorId: '33333333-3333-4333-8333-333333333333',
    tripStatus: 'IN_PROGRESS',
    alternativeRouteId: '44444444-4444-4444-8444-444444444444',
    affectedBookings: [{
      bookingId: '55555555-5555-4555-8555-555555555555',
      candidateStops: [{
        stopId: STOP_ID,
        stationId: null,
        stationName: 'Điểm dừng thay thế',
        sequence: 1,
        estimatedArrivalAt: '2026-08-10T01:45:00Z',
      }],
    }],
  };
}

function raw(payload: unknown): ConsumeMessage {
  return {
    content: Buffer.from(JSON.stringify(payload)),
    fields: {} as ConsumeMessage['fields'],
    properties: {
      messageId: EVENT_ID,
      correlationId: undefined,
    } as ConsumeMessage['properties'],
  };
}
