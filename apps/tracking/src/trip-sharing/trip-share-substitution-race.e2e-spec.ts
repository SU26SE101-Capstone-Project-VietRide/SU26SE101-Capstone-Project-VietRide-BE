import { Test, type TestingModule } from '@nestjs/testing';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import { RedisService } from '@vietride/nest-redis';
import type { ConsumeMessage } from 'amqplib';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import { TripShareGrantRepository } from './trip-share-grant.repository';
import { TripShareMessageIdempotencyRepository } from './trip-share-message-idempotency.repository';
import { TripShareRealtimePublisher } from './trip-share-realtime.publisher';
import { TripShareSubstitutionStateRepository } from './trip-share-substitution-state.repository';
import { TripTerminalShareConsumer } from './trip-terminal-share.consumer';
import { TripVehicleSubstitutedShareConsumer } from './trip-vehicle-substituted-share.consumer';

const OLD_TRIP_ID = '11111111-1111-4111-8111-111111111111';
const NEW_TRIP_ID = '22222222-2222-4222-8222-222222222222';
const OPERATOR_ID = '33333333-3333-4333-8333-333333333333';

describe('Trip-share vehicle-substitution event race (in-process e2e)', () => {
  let module: TestingModule;
  let subscriptions: Map<string, (payload: unknown, raw: ConsumeMessage) => Promise<void>>;
  let redis: InMemoryRedis;
  let grantTripId: string;
  let revoked: boolean;
  let transferTrip: jest.Mock;
  let revokeTrip: jest.Mock;

  beforeEach(async () => {
    subscriptions = new Map();
    redis = new InMemoryRedis();
    grantTripId = OLD_TRIP_ID;
    revoked = false;
    transferTrip = jest.fn().mockResolvedValue(undefined);
    revokeTrip = jest.fn().mockResolvedValue(undefined);
    const grants = {
      hasActiveForTrip: jest.fn(async (tripId: string) => !revoked && grantTripId === tripId),
      revokeAllActiveForTrip: jest.fn(async (tripId: string) => {
        if (revoked || grantTripId !== tripId) return 0;
        revoked = true;
        return 1;
      }),
      transferActiveGrants: jest.fn(async (oldTripId: string, newTripId: string) => {
        if (revoked || grantTripId !== oldTripId) return 0;
        grantTripId = newTripId;
        return 1;
      }),
    };

    module = await Test.createTestingModule({
      providers: [
        TripTerminalShareConsumer,
        TripVehicleSubstitutedShareConsumer,
        TripShareMessageIdempotencyRepository,
        TripShareSubstitutionStateRepository,
        {
          provide: ENV_TOKEN,
          useValue: { TRACKING_SHARE_TOKEN_TTL_SECONDS: 86_400 } as Env,
        },
        {
          provide: RabbitMqConsumer,
          useValue: {
            subscribe: jest.fn(async (
              _queue: string,
              routingKey: string,
              handler: (payload: unknown, raw: ConsumeMessage) => Promise<void>,
            ) => subscriptions.set(routingKey, handler)),
          },
        },
        { provide: RedisService, useValue: { getClient: () => redis } },
        { provide: TripShareGrantRepository, useValue: grants },
        {
          provide: TripShareRealtimePublisher,
          useValue: { transferTrip, revokeTrip },
        },
      ],
    }).compile();
    await module.init();
  });

  afterEach(async () => module.close());

  it.each(['disrupted-first', 'substituted-first'])(
    'preserves the same grant when events arrive %s',
    async (order) => {
      const disrupted = disruptedPayload();
      const substituted = substitutedPayload();
      if (order === 'disrupted-first') {
        await publish('trip.trip.disrupted', disrupted);
        expect(await redis.get(`tracking:trip-share:substitution:pending:${OLD_TRIP_ID}`))
          .toBe(disrupted.occurredAt);
        await publish('trip.trip.vehicle_substituted', substituted);
      } else {
        await publish('trip.trip.vehicle_substituted', substituted);
        await publish('trip.trip.disrupted', disrupted);
      }

      expect(grantTripId).toBe(NEW_TRIP_ID);
      expect(revoked).toBe(false);
      expect(revokeTrip).not.toHaveBeenCalled();
      expect(transferTrip).toHaveBeenCalledWith(
        OLD_TRIP_ID,
        NEW_TRIP_ID,
        substituted.occurredAt,
      );
      expect(await redis.get(`tracking:trip-share:substitution:next:${OLD_TRIP_ID}`))
        .toBe(NEW_TRIP_ID);
      expect(await redis.get(`tracking:trip-share:substitution:pending:${OLD_TRIP_ID}`))
        .toBeNull();

      await publish('trip.trip.disrupted', disrupted);
      await publish('trip.trip.vehicle_substituted', substituted);
      expect(transferTrip).toHaveBeenCalledTimes(1);
    },
  );

  async function publish(routingKey: string, payload: Record<string, unknown>): Promise<void> {
    const handler = subscriptions.get(routingKey);
    if (!handler) throw new Error(`Missing handler for ${routingKey}`);
    await handler(payload, raw(payload));
  }
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

  async del(key: string): Promise<number> {
    return this.values.delete(key) ? 1 : 0;
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

function disruptedPayload(): Record<string, unknown> {
  return {
    eventId: '44444444-4444-4444-8444-444444444444',
    occurredAt: '2026-08-31T08:00:00.000Z',
    tripId: OLD_TRIP_ID,
    operatorId: OPERATOR_ID,
    terminalAt: '2026-08-31T08:00:00.000Z',
    hasSubstitution: true,
    reason: 'Vehicle breakdown',
  };
}

function substitutedPayload(): Record<string, unknown> {
  return {
    eventId: '55555555-5555-4555-8555-555555555555',
    occurredAt: '2026-08-31T08:00:00.000Z',
    substitutionId: '55555555-5555-4555-8555-555555555555',
    disruptedAt: '2026-08-31T08:00:00.000Z',
    operatorId: OPERATOR_ID,
    oldTripId: OLD_TRIP_ID,
    oldTripStatus: 'DISRUPTED',
    oldVehicleId: '66666666-6666-4666-8666-666666666666',
    newTripId: NEW_TRIP_ID,
    newTripStatus: 'BOARDING',
    newVehicleId: '77777777-7777-4777-8777-777777777777',
    newVehiclePlateNumber: '51B-123.45',
    newTripDepartureDateTime: '2026-08-31T08:10:00.000Z',
    actorUserId: '88888888-8888-4888-8888-888888888888',
    reason: 'Vehicle breakdown',
    notifyPassengers: true,
    mappings: [],
  };
}

function raw(payload: Record<string, unknown>): ConsumeMessage {
  return {
    content: Buffer.from(JSON.stringify(payload)),
    properties: {
      messageId: payload.eventId as string,
      correlationId: payload.eventId as string,
      headers: {},
    },
  } as unknown as ConsumeMessage;
}
