import { Test, type TestingModule } from '@nestjs/testing';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import { RedisService } from '@vietride/nest-redis';
import type { ConsumeMessage } from 'amqplib';
import { TrackingPrismaService } from '../prisma/tracking-prisma.service';
import { TripShareGrantRepository } from './trip-share-grant.repository';
import { TripShareMessageIdempotencyRepository } from './trip-share-message-idempotency.repository';
import { TripShareRealtimePublisher } from './trip-share-realtime.publisher';
import { TripShareSubstitutionStateRepository } from './trip-share-substitution-state.repository';
import { TripTerminalShareConsumer } from './trip-terminal-share.consumer';

const OPERATOR_ID = '99999999-9999-4999-8999-999999999999';

describe('TripTerminalShareConsumer (in-process e2e)', () => {
  let module: TestingModule;
  let subscriptions: Map<string, (payload: unknown, raw: ConsumeMessage) => Promise<void>>;
  let updateMany: jest.Mock;
  let revokeTrip: jest.Mock;
  let redis: InMemoryRedis;
  let markPending: jest.Mock;

  beforeEach(async () => {
    subscriptions = new Map();
    updateMany = jest.fn().mockResolvedValue({ count: 1 });
    revokeTrip = jest.fn().mockResolvedValue(undefined);
    redis = new InMemoryRedis();
    markPending = jest.fn().mockResolvedValue(undefined);

    module = await Test.createTestingModule({
      providers: [
        TripTerminalShareConsumer,
        TripShareMessageIdempotencyRepository,
        TripShareGrantRepository,
        {
          provide: RabbitMqConsumer,
          useValue: {
            subscribe: jest.fn(async (
              _queue: string,
              routingKey: string,
              handler: (payload: unknown, raw: ConsumeMessage) => Promise<void>,
            ) => {
              subscriptions.set(routingKey, handler);
            }),
          },
        },
        { provide: RedisService, useValue: { getClient: () => redis } },
        {
          provide: TrackingPrismaService,
          useValue: {
            tripShareGrant: {
              updateMany,
              count: jest.fn().mockResolvedValue(1),
            },
          },
        },
        { provide: TripShareRealtimePublisher, useValue: { revokeTrip } },
        {
          provide: TripShareSubstitutionStateRepository,
          useValue: { markPending },
        },
      ],
    }).compile();

    await module.init();
  });

  afterEach(async () => {
    await module.close();
  });

  it.each([
    ['trip.trip.completed', completedPayload('11111111-1111-4111-8111-111111111111', '21111111-1111-4111-8111-111111111111')],
    ['trip.trip.cancelled', cancelledPayload('12222222-2222-4222-8222-222222222222', '22222222-2222-4222-8222-222222222222')],
    ['trip.trip.disrupted', disruptedPayload('13333333-3333-4333-8333-333333333333', '23333333-3333-4333-8333-333333333333')],
  ])('revokes and disconnects %s exactly once across duplicate deliveries', async (routingKey, payload) => {
    const handler = subscriptions.get(routingKey);
    if (!handler) throw new Error(`Missing handler for ${routingKey}`);
    const message = raw(payload, `broker-${routingKey}`);

    await handler(payload, message);
    await handler(payload, message);

    expect(updateMany).toHaveBeenCalledTimes(1);
    expect(updateMany).toHaveBeenCalledWith({
      where: { tripId: payload.tripId, revokedAt: null },
      data: { revokedAt: expect.any(Date), revokeReason: 'TRIP_TERMINATED' },
    });
    expect(revokeTrip).toHaveBeenCalledTimes(1);
    expect(revokeTrip).toHaveBeenCalledWith(payload.tripId, 'TRIP_ENDED');
    expect(redis.values.get(`tracking:trip-share:event:processed:${payload.eventId}`)).toBe('1');
    expect(redis.values.has(`tracking:trip-share:event:processing:${payload.eventId}`)).toBe(false);
  });

  it('drops malformed events after idempotently marking their broker identity', async () => {
    const handler = subscriptions.get('trip.trip.cancelled');
    if (!handler) throw new Error('Missing cancelled handler');
    const malformed = { tripId: 'not-a-uuid' };

    await handler(malformed, raw(malformed, 'malformed-terminal-event'));

    expect(updateMany).not.toHaveBeenCalled();
    expect(revokeTrip).not.toHaveBeenCalled();
    expect(redis.values.get(
      'tracking:trip-share:event:processed:malformed-terminal-event',
    )).toBe('1');
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
}

function completedPayload(eventId: string, tripId: string) {
  return {
    eventId,
    occurredAt: '2026-08-03T10:00:00.000Z',
    tripId,
    operatorId: OPERATOR_ID,
    terminalAt: '2026-08-03T10:00:00.000Z',
    hasSubstitution: false,
  };
}

function cancelledPayload(eventId: string, tripId: string) {
  return {
    eventId,
    occurredAt: '2026-08-03T10:00:00.000Z',
    tripId,
    operatorId: OPERATOR_ID,
    cancelledAt: '2026-08-03T10:00:00.000Z',
    cancelReason: 'OPERATOR_CANCELLED',
  };
}

function disruptedPayload(eventId: string, tripId: string) {
  return { ...completedPayload(eventId, tripId), reason: 'VEHICLE_BREAKDOWN' };
}

function raw(payload: unknown, messageId: string): ConsumeMessage {
  return {
    content: Buffer.from(JSON.stringify(payload)),
    properties: { messageId, headers: {} },
  } as unknown as ConsumeMessage;
}
