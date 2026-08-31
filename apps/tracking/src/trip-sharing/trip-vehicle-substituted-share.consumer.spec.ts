import type { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import type { TripShareGrantRepository } from './trip-share-grant.repository';
import type { TripShareMessageIdempotencyRepository } from './trip-share-message-idempotency.repository';
import type { TripShareRealtimePublisher } from './trip-share-realtime.publisher';
import type { TripShareSubstitutionStateRepository } from './trip-share-substitution-state.repository';
import { TripVehicleSubstitutedShareConsumer } from './trip-vehicle-substituted-share.consumer';

const EVENT_ID = '11111111-1111-4111-8111-111111111111';
const OLD_TRIP_ID = '22222222-2222-4222-8222-222222222222';
const NEW_TRIP_ID = '33333333-3333-4333-8333-333333333333';

describe('TripVehicleSubstitutedShareConsumer', () => {
  let subscribe: jest.Mock;
  let idempotency: jest.Mocked<TripShareMessageIdempotencyRepository>;
  let grants: jest.Mocked<TripShareGrantRepository>;
  let substitutions: jest.Mocked<TripShareSubstitutionStateRepository>;
  let realtime: jest.Mocked<TripShareRealtimePublisher>;
  let consumer: TripVehicleSubstitutedShareConsumer;

  beforeEach(() => {
    subscribe = jest.fn().mockResolvedValue(undefined);
    idempotency = {
      isProcessed: jest.fn().mockResolvedValue(false),
      acquire: jest.fn().mockResolvedValue('owner-token'),
      markProcessed: jest.fn().mockResolvedValue(true),
      release: jest.fn().mockResolvedValue(true),
    } as unknown as jest.Mocked<TripShareMessageIdempotencyRepository>;
    grants = {
      transferActiveGrants: jest.fn().mockResolvedValue(2),
    } as unknown as jest.Mocked<TripShareGrantRepository>;
    substitutions = {
      storeAlias: jest.fn().mockResolvedValue(undefined),
      clearPending: jest.fn().mockResolvedValue(undefined),
    } as unknown as jest.Mocked<TripShareSubstitutionStateRepository>;
    realtime = {
      transferTrip: jest.fn().mockResolvedValue(undefined),
    } as unknown as jest.Mocked<TripShareRealtimePublisher>;
    consumer = new TripVehicleSubstitutedShareConsumer(
      { subscribe } as unknown as RabbitMqConsumer,
      idempotency,
      grants,
      substitutions,
      realtime,
    );
  });

  it('registers the exact durable retry subscription', async () => {
    await consumer.onModuleInit();

    expect(subscribe).toHaveBeenCalledWith(
      'tracking-trip-share-vehicle-substituted',
      'trip.trip.vehicle_substituted',
      expect.any(Function),
      { prefetch: 1, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000 },
    );
  });

  it('transfers DB grants, alias and sockets before completing idempotency', async () => {
    const order: string[] = [];
    grants.transferActiveGrants.mockImplementation(async () => { order.push('db'); return 2; });
    substitutions.storeAlias.mockImplementation(async () => { order.push('alias'); });
    realtime.transferTrip.mockImplementation(async () => { order.push('socket'); });
    substitutions.clearPending.mockImplementation(async () => { order.push('pending'); });
    idempotency.markProcessed.mockImplementation(async () => { order.push('mark'); return true; });

    await invoke(consumer, subscribe, payload());

    expect(grants.transferActiveGrants).toHaveBeenCalledWith(
      OLD_TRIP_ID,
      NEW_TRIP_ID,
      expect.any(Date),
    );
    expect(substitutions.storeAlias).toHaveBeenCalledWith(OLD_TRIP_ID, NEW_TRIP_ID);
    expect(realtime.transferTrip).toHaveBeenCalledWith(
      OLD_TRIP_ID,
      NEW_TRIP_ID,
      '2026-08-31T08:00:00.000Z',
    );
    expect(order).toEqual(['db', 'alias', 'socket', 'pending', 'mark']);
  });

  it('skips a duplicate before acquiring the processing lock', async () => {
    idempotency.isProcessed.mockResolvedValueOnce(true);

    await invoke(consumer, subscribe, payload());

    expect(idempotency.acquire).not.toHaveBeenCalled();
    expect(grants.transferActiveGrants).not.toHaveBeenCalled();
  });

  it('drops malformed payload after marking it processed', async () => {
    const malformed = { eventId: EVENT_ID, oldTripId: OLD_TRIP_ID };

    await invoke(consumer, subscribe, malformed);

    expect(idempotency.markProcessed).toHaveBeenCalledWith(EVENT_ID, 'owner-token');
    expect(grants.transferActiveGrants).not.toHaveBeenCalled();
  });

  it('throws on lock contention so RabbitMQ retries', async () => {
    idempotency.acquire.mockResolvedValueOnce(null);

    await expect(invoke(consumer, subscribe, payload())).rejects.toThrow(
      'TRIP_SHARE_VEHICLE_SUBSTITUTED_EVENT_LOCKED',
    );
  });

  it.each(['db', 'alias', 'socket', 'pending'])(
    'releases the lock and retries after a transient %s failure',
    async (target) => {
      const failure = new Error(`${target} unavailable`);
      if (target === 'db') grants.transferActiveGrants.mockRejectedValueOnce(failure);
      if (target === 'alias') substitutions.storeAlias.mockRejectedValueOnce(failure);
      if (target === 'socket') realtime.transferTrip.mockRejectedValueOnce(failure);
      if (target === 'pending') substitutions.clearPending.mockRejectedValueOnce(failure);

      await expect(invoke(consumer, subscribe, payload())).rejects.toBe(failure);

      expect(idempotency.release).toHaveBeenCalledWith(EVENT_ID, 'owner-token');
      expect(idempotency.markProcessed).not.toHaveBeenCalled();
    },
  );

  it('replays alias and realtime after a prior DB commit moved zero remaining grants', async () => {
    grants.transferActiveGrants.mockResolvedValueOnce(0);

    await invoke(consumer, subscribe, payload());

    expect(substitutions.storeAlias).toHaveBeenCalled();
    expect(realtime.transferTrip).toHaveBeenCalled();
    expect(idempotency.markProcessed).toHaveBeenCalled();
  });
});

async function invoke(
  consumer: TripVehicleSubstitutedShareConsumer,
  subscribe: jest.Mock,
  event: unknown,
): Promise<void> {
  if (subscribe.mock.calls.length === 0) await consumer.onModuleInit();
  await subscribe.mock.calls[0][2](event, raw(event));
}

function payload(): Record<string, unknown> {
  return {
    eventId: EVENT_ID,
    occurredAt: '2026-08-31T08:00:00.000Z',
    substitutionId: EVENT_ID,
    disruptedAt: '2026-08-31T08:00:00.000Z',
    operatorId: '44444444-4444-4444-8444-444444444444',
    oldTripId: OLD_TRIP_ID,
    oldTripStatus: 'DISRUPTED',
    oldVehicleId: '55555555-5555-4555-8555-555555555555',
    newTripId: NEW_TRIP_ID,
    newTripStatus: 'BOARDING',
    newVehicleId: '66666666-6666-4666-8666-666666666666',
    newVehiclePlateNumber: '51B-123.45',
    newTripDepartureDateTime: '2026-08-31T08:10:00.000Z',
    actorUserId: '77777777-7777-4777-8777-777777777777',
    reason: 'Vehicle breakdown',
    notifyPassengers: true,
    mappings: [],
  };
}

function raw(event: unknown): ConsumeMessage {
  return {
    content: Buffer.from(JSON.stringify(event)),
    properties: { messageId: EVENT_ID, headers: {} },
  } as unknown as ConsumeMessage;
}
