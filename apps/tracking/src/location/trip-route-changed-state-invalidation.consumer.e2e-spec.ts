import { Test, type TestingModule } from '@nestjs/testing';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import { RedisService } from '@vietride/nest-redis';
import type { ConsumeMessage } from 'amqplib';
import type { Env } from '../config/env.schema';
import { TRIP_DATA_PROVIDER } from '../eta/eta.constants';
import type { EtaProvider } from '../eta/eta-provider';
import { EtaService } from '../eta/eta.service';
import type { TripDataProvider } from '../eta/trip-data.provider';
import { ROUTE_GEOMETRY_PROVIDER } from '../off-route/off-route.constants';
import type { RouteGeometryProvider } from '../off-route/route-geometry.provider';
import { OffRouteService } from '../off-route/off-route.service';
import { RouteStateGenerationRegistry } from '../route-state/route-state-generation.registry';
import { trackingTripDelayStateKey } from '../trip-delay/trip-delay.constants';
import { TripRouteChangedStateInvalidationConsumer } from './trip-route-changed-state-invalidation.consumer';

const EVENT_ID = '11111111-1111-4111-8111-111111111111';
const TRIP_ID = '22222222-2222-4222-8222-222222222222';

interface InMemoryRedisTransaction {
  set(key: string, value: string): InMemoryRedisTransaction;
  exec(): Promise<[]>;
}

describe('TripRouteChangedStateInvalidationConsumer (in-process e2e)', () => {
  let module: TestingModule;
  let subscriptions: Map<string, (payload: unknown, raw: ConsumeMessage) => Promise<void>>;
  let redis: InMemoryRedis;
  let tripData: jest.Mocked<TripDataProvider>;
  let routeGeometry: jest.Mocked<RouteGeometryProvider>;
  let routeStateGeneration: RouteStateGenerationRegistry;

  beforeEach(async () => {
    subscriptions = new Map();
    redis = new InMemoryRedis();
    tripData = {
      getRouteStops: jest.fn(),
      invalidateRouteStops: jest.fn(),
    };
    routeGeometry = {
      peekCachedRouteGeometry: jest.fn(),
      getRouteGeometry: jest.fn(),
      invalidateRouteGeometry: jest.fn(),
    };

    module = await Test.createTestingModule({
      providers: [
        TripRouteChangedStateInvalidationConsumer,
        {
          provide: RabbitMqConsumer,
          useValue: {
            subscribe: jest.fn(
              async (
                _queue: string,
                routingKey: string,
                handler: (payload: unknown, raw: ConsumeMessage) => Promise<void>,
              ) => {
                subscriptions.set(routingKey, handler);
              },
            ),
          },
        },
        { provide: RedisService, useValue: { getClient: () => redis } },
        { provide: TRIP_DATA_PROVIDER, useValue: tripData },
        { provide: ROUTE_GEOMETRY_PROVIDER, useValue: routeGeometry },
        {
          provide: OffRouteService,
          useValue: {
            clearRuntimeState: jest.fn(async (tripId: string) => {
              redis.values.delete(`tracking:off_route_since:${tripId}`);
            }),
          },
        },
        RouteStateGenerationRegistry,
      ],
    }).compile();

    await module.init();
    routeStateGeneration = module.get(RouteStateGenerationRegistry);
  });

  afterEach(async () => {
    await module.close();
  });

  it('invalidates all route-dependent state exactly once across duplicate deliveries', async () => {
    redis.values.set(`tracking:eta_state:${TRIP_ID}`, 'state');
    redis.values.set(`tracking:off_route_since:${TRIP_ID}`, 'timestamp');
    redis.values.set(`tracking:eta:${TRIP_ID}:stop-1`, 'eta-1');
    redis.values.set(`tracking:eta:${TRIP_ID}:stop-2`, 'eta-2');
    redis.values.set(trackingTripDelayStateKey(TRIP_ID), 'old-delay-pointer');
    redis.values.set(trackingTripDelayStateKey(TRIP_ID, 'old-stop'), 'old-delay-stop-state');
    redis.values.set('tracking:eta:another-trip:stop-1', 'keep');
    redis.values.set(trackingTripDelayStateKey('another-trip'), 'keep-delay');

    const handler = subscriptions.get('trip.trip.route_changed');
    if (!handler) throw new Error('Missing trip route-changed handler');
    const payload = validPayload();
    const message = raw(payload);

    await handler(payload, message);
    await handler(payload, message);

    expect(tripData.invalidateRouteStops).toHaveBeenCalledTimes(1);
    expect(tripData.invalidateRouteStops).toHaveBeenCalledWith(TRIP_ID);
    expect(routeGeometry.invalidateRouteGeometry).toHaveBeenCalledTimes(1);
    expect(routeGeometry.invalidateRouteGeometry).toHaveBeenCalledWith(TRIP_ID);
    expect(redis.values.has(`tracking:eta_state:${TRIP_ID}`)).toBe(false);
    expect(redis.values.has(`tracking:off_route_since:${TRIP_ID}`)).toBe(false);
    expect(redis.values.has(`tracking:eta:${TRIP_ID}:stop-1`)).toBe(false);
    expect(redis.values.has(`tracking:eta:${TRIP_ID}:stop-2`)).toBe(false);
    expect(redis.values.has(trackingTripDelayStateKey(TRIP_ID))).toBe(false);
    expect(redis.values.has(trackingTripDelayStateKey(TRIP_ID, 'old-stop'))).toBe(false);
    expect(redis.values.get('tracking:eta:another-trip:stop-1')).toBe('keep');
    expect(redis.values.get(trackingTripDelayStateKey('another-trip'))).toBe('keep-delay');
    expect(routeStateGeneration.capture(TRIP_ID)).toBe(1);
    expect(redis.values.get(`tracking:trip_route_changed:processed:${EVENT_ID}`)).toBe('1');
    expect(redis.values.has(`tracking:trip_route_changed:processing:${EVENT_ID}`)).toBe(false);
  });

  it('fences an ETA calculation that finishes after route-change invalidation', async () => {
    let resolveProvider:
      | ((value: { distanceMeters: number; etaMinutes: number }) => void)
      | undefined;
    let providerStarted!: () => void;
    const started = new Promise<void>((resolve) => {
      providerStarted = resolve;
    });
    const localProvider: EtaProvider = {
      calculate: jest.fn(async () => {
        providerStarted();
        return new Promise((resolve) => {
          resolveProvider = resolve;
        });
      }),
    };
    const noGoogleProvider: EtaProvider = { calculate: jest.fn(async () => null) };
    tripData.getRouteStops.mockResolvedValue([
      {
        stopId: '66666666-6666-4666-8666-666666666666',
        latitude: 10.82,
        longitude: 106.66,
        sequence: 1,
      },
    ]);
    routeGeometry.peekCachedRouteGeometry.mockReturnValue({
      tripId: TRIP_ID,
      points: [
        { latitude: 10.7, longitude: 106.66 },
        { latitude: 10.9, longitude: 106.66 },
      ],
    });
    const etaService = new EtaService(
      { getClient: () => redis } as unknown as RedisService,
      tripData,
      routeGeometry,
      noGoogleProvider,
      localProvider,
      {
        ROUTING_PROVIDER: 'LOCAL',
        GOONG_API_KEY: '',
        TRACKING_ETA_MIN_INTERVAL_SECONDS: 60,
        TRACKING_ETA_CACHE_TTL_SECONDS: 60,
      } as Env,
      routeStateGeneration,
    );

    const etaPromise = etaService.handleGpsUpdate({
      tripId: TRIP_ID,
      latitude: 10.76,
      longitude: 106.66,
      speedKmh: 40,
      recordedAt: '2026-08-10T01:00:00Z',
    });
    await started;

    const handler = subscriptions.get('trip.trip.route_changed');
    if (!handler) throw new Error('Missing trip route-changed handler');
    const payload = validPayload();
    await handler(payload, raw(payload));

    if (!resolveProvider) throw new Error('ETA provider did not start');
    resolveProvider({ distanceMeters: 7_500, etaMinutes: 12 });

    await expect(etaPromise).resolves.toBeNull();
    expect(redis.values.has(`tracking:eta_state:${TRIP_ID}`)).toBe(false);
    expect([...redis.values.keys()].some((key) => key.startsWith(`tracking:eta:${TRIP_ID}:`))).toBe(
      false,
    );
  });
});

class InMemoryRedis {
  readonly values = new Map<string, string>();

  async get(key: string): Promise<string | null> {
    return this.values.get(key) ?? null;
  }

  async set(key: string, value: string, ...args: unknown[]): Promise<string | null> {
    if (args.includes('NX') && this.values.has(key)) return null;
    this.values.set(key, value);
    return 'OK';
  }

  async del(...keys: string[]): Promise<number> {
    let deleted = 0;
    for (const key of keys) {
      if (this.values.delete(key)) deleted += 1;
    }
    return deleted;
  }

  async scan(
    _cursor: string,
    _matchKeyword: string,
    pattern: string,
    _countKeyword: string,
    _count: number,
  ): Promise<[string, string[]]> {
    void _countKeyword;
    void _count;
    const prefix = pattern.endsWith('*') ? pattern.slice(0, -1) : pattern;
    return ['0', [...this.values.keys()].filter((key) => key.startsWith(prefix))];
  }

  async eval(_script: string, numberOfKeys: number, ...args: unknown[]): Promise<number> {
    const processingKey = args[0] as string;
    if (numberOfKeys === 2) {
      const processedKey = args[1] as string;
      const ownerToken = args[2] as string;
      if (this.values.get(processingKey) !== ownerToken) return 0;
      this.values.set(processedKey, '1');
      this.values.delete(processingKey);
      return 1;
    }

    const ownerToken = args[1] as string;
    if (this.values.get(processingKey) !== ownerToken) return 0;
    this.values.delete(processingKey);
    return 1;
  }

  multi(): InMemoryRedisTransaction {
    const writes: Array<[string, string]> = [];
    const transaction: InMemoryRedisTransaction = {
      set: (key: string, value: string) => {
        writes.push([key, value]);
        return transaction;
      },
      exec: async (): Promise<[]> => {
        for (const [key, value] of writes) this.values.set(key, value);
        return [];
      },
    };
    return transaction;
  }
}

function validPayload(): Record<string, unknown> {
  return {
    eventId: EVENT_ID,
    occurredAt: '2026-08-10T01:00:00Z',
    tripId: TRIP_ID,
    operatorId: '33333333-3333-4333-8333-333333333333',
    tripStatus: 'IN_PROGRESS',
    alternativeRouteId: '44444444-4444-4444-8444-444444444444',
    affectedBookings: [],
  };
}

function raw(payload: unknown): ConsumeMessage {
  return {
    content: Buffer.from(JSON.stringify(payload)),
    properties: { messageId: EVENT_ID, headers: {} },
  } as unknown as ConsumeMessage;
}
