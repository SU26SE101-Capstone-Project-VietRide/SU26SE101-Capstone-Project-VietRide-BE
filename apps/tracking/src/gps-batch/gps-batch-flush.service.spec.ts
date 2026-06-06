import { RedisService } from '@vietride/nest-redis';
import { trackingGpsBufferKey } from '../location/location.constants';
import { TrackingPrismaService } from '../prisma/tracking-prisma.service';
import { GpsBatchFlushService } from './gps-batch-flush.service';

const FIRST_TRIP_ID = '11111111-1111-4111-8111-111111111111';
const SECOND_TRIP_ID = '22222222-2222-4222-8222-222222222222';

describe('GpsBatchFlushService', () => {
  let service: GpsBatchFlushService;
  let redisClient: RedisClientMock;
  let prisma: PrismaMock;

  beforeEach(() => {
    redisClient = createRedisClientMock();
    prisma = createPrismaMock();
    service = new GpsBatchFlushService(
      { getClient: jest.fn(() => redisClient) } as unknown as RedisService,
      prisma as unknown as TrackingPrismaService,
    );
  });

  it('flushes GPS buffers for two active trips', async () => {
    redisClient.activeTripIds = [FIRST_TRIP_ID, SECOND_TRIP_ID];
    redisClient.buffers.set(trackingGpsBufferKey(FIRST_TRIP_ID), [
      JSON.stringify(createGpsPayload(FIRST_TRIP_ID, '2026-06-03T10:00:00.000Z')),
    ]);
    redisClient.buffers.set(trackingGpsBufferKey(SECOND_TRIP_ID), [
      JSON.stringify(createGpsPayload(SECOND_TRIP_ID, '2026-06-03T10:01:00.000Z')),
    ]);

    await expect(service.flushOnce()).resolves.toBe(2);

    expect(prisma.gpsTrail.createMany).toHaveBeenCalledTimes(2);
    expect(redisClient.del).toHaveBeenCalledWith(trackingGpsBufferKey(FIRST_TRIP_ID));
    expect(redisClient.del).toHaveBeenCalledWith(trackingGpsBufferKey(SECOND_TRIP_ID));
  });

  it('does not insert or clear an empty buffer', async () => {
    redisClient.activeTripIds = [FIRST_TRIP_ID];

    await expect(service.flushOnce()).resolves.toBe(0);

    expect(prisma.gpsTrail.createMany).not.toHaveBeenCalled();
    expect(redisClient.del).not.toHaveBeenCalled();
  });

  it('skips malformed rows and inserts valid rows', async () => {
    redisClient.activeTripIds = [FIRST_TRIP_ID];
    redisClient.buffers.set(trackingGpsBufferKey(FIRST_TRIP_ID), [
      '{bad-json',
      JSON.stringify({ tripId: FIRST_TRIP_ID, latitude: 'bad', longitude: 106.660172, recordedAt: 'bad' }),
      JSON.stringify(createGpsPayload(FIRST_TRIP_ID, '2026-06-03T10:00:00.000Z')),
    ]);

    await expect(service.flushOnce()).resolves.toBe(1);

    expect(prisma.gpsTrail.createMany).toHaveBeenCalledWith({
      data: [
        {
          tripId: FIRST_TRIP_ID,
          latitude: 10.762622,
          longitude: 106.660172,
          recordedAt: new Date('2026-06-03T10:00:00.000Z'),
          speedKmh: 42,
          headingDeg: 90,
        },
      ],
    });
    expect(redisClient.del).toHaveBeenCalledWith(trackingGpsBufferKey(FIRST_TRIP_ID));
  });

  it('does not clear the buffer when DB insert fails', async () => {
    redisClient.activeTripIds = [FIRST_TRIP_ID];
    redisClient.buffers.set(trackingGpsBufferKey(FIRST_TRIP_ID), [
      JSON.stringify(createGpsPayload(FIRST_TRIP_ID, '2026-06-03T10:00:00.000Z')),
    ]);
    prisma.gpsTrail.createMany.mockRejectedValueOnce(new Error('DB_INSERT_FAILED'));

    await expect(service.flushOnce()).rejects.toThrow('DB_INSERT_FAILED');

    expect(redisClient.del).not.toHaveBeenCalled();
  });
});

interface RedisClientMock {
  activeTripIds: string[];
  buffers: Map<string, string[]>;
  smembers: jest.MockedFunction<(key: string) => Promise<string[]>>;
  lrange: jest.MockedFunction<(key: string, start: number, stop: number) => Promise<string[]>>;
  del: jest.MockedFunction<(key: string) => Promise<number>>;
}

interface PrismaMock {
  gpsTrail: {
    createMany: jest.MockedFunction<(args: unknown) => Promise<{ count: number }>>;
  };
}

function createRedisClientMock(): RedisClientMock {
  const client: RedisClientMock = {
    activeTripIds: [],
    buffers: new Map<string, string[]>(),
    smembers: jest.fn(async (key: string) => {
      void key;
      return client.activeTripIds;
    }),
    lrange: jest.fn(async (key: string, start: number, stop: number) => {
      void start;
      void stop;
      return client.buffers.get(key) ?? [];
    }),
    del: jest.fn(async (key: string) => {
      client.buffers.delete(key);
      return 1;
    }),
  };
  return client;
}

function createPrismaMock(): PrismaMock {
  return {
    gpsTrail: {
      createMany: jest.fn(async (args: unknown) => {
        const data = (args as { data: unknown[] }).data;
        return { count: data.length };
      }),
    },
  };
}

function createGpsPayload(tripId: string, recordedAt: string): Record<string, unknown> {
  return {
    tripId,
    latitude: 10.762622,
    longitude: 106.660172,
    speedKmh: 42,
    headingDeg: 90,
    recordedAt,
  };
}
