import { RedisService } from '@vietride/nest-redis';
import { GPS_BATCH_IDLE_CYCLES_BEFORE_PRUNE } from './gps-batch.constants';
import { trackingGpsBufferKey, trackingGpsIdleKey, trackingGpsProcessingKey } from '../location/location.constants';
import { TrackingPrismaService } from '../prisma/tracking-prisma.service';
import { GpsBatchFlushService } from './gps-batch-flush.service';

const FIRST_TRIP_ID = '11111111-1111-4111-8111-111111111111';
const SECOND_TRIP_ID = '22222222-2222-4222-8222-222222222222';

function bufferKey(): string {
  return trackingGpsBufferKey(FIRST_TRIP_ID);
}
function processingKey(): string {
  return trackingGpsProcessingKey(FIRST_TRIP_ID);
}
function idleKey(): string {
  return trackingGpsIdleKey(FIRST_TRIP_ID);
}
function bufferKey2(): string {
  return trackingGpsBufferKey(SECOND_TRIP_ID);
}
function processingKey2(): string {
  return trackingGpsProcessingKey(SECOND_TRIP_ID);
}

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
    redisClient.buffers.set(bufferKey(), [
      JSON.stringify(createGpsPayload(FIRST_TRIP_ID, '2026-06-03T10:00:00.000Z')),
    ]);
    redisClient.buffers.set(bufferKey2(), [
      JSON.stringify(createGpsPayload(SECOND_TRIP_ID, '2026-06-03T10:01:00.000Z')),
    ]);

    await expect(service.flushOnce()).resolves.toBe(2);

    expect(prisma.gpsTrail.createMany).toHaveBeenCalledTimes(2);
    expect(redisClient.eval).toHaveBeenCalledTimes(2);
    // Buffer should be renamed to processing
    expect(redisClient.buffers.has(processingKey())).toBe(false);
    expect(redisClient.buffers.has(processingKey2())).toBe(false);
  });

  it('does not insert for an empty buffer', async () => {
    redisClient.activeTripIds = [FIRST_TRIP_ID];

    await expect(service.flushOnce()).resolves.toBe(0);

    expect(prisma.gpsTrail.createMany).not.toHaveBeenCalled();
    expect(redisClient.eval).toHaveBeenCalledTimes(2);
  });

  it('skips malformed rows and deletes processing key', async () => {
    redisClient.activeTripIds = [FIRST_TRIP_ID];
    redisClient.buffers.set(bufferKey(), [
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
      skipDuplicates: true,
    });
  });

  it('preserves processing key when DB insert fails for retry', async () => {
    redisClient.activeTripIds = [FIRST_TRIP_ID];
    redisClient.buffers.set(bufferKey(), [
      JSON.stringify(createGpsPayload(FIRST_TRIP_ID, '2026-06-03T10:00:00.000Z')),
    ]);
    prisma.gpsTrail.createMany.mockRejectedValueOnce(new Error('DB_INSERT_FAILED'));

    await expect(service.flushOnce()).resolves.toBe(0);

    // Processing key kept — next flush will retry
    const processingRows = redisClient.buffers.get(processingKey());
    expect(processingRows).toHaveLength(1);
  });

  it('continues flushing other trips when one trip insert fails', async () => {
    redisClient.activeTripIds = [FIRST_TRIP_ID, SECOND_TRIP_ID];
    redisClient.buffers.set(bufferKey(), [
      JSON.stringify(createGpsPayload(FIRST_TRIP_ID, '2026-06-03T10:00:00.000Z')),
    ]);
    redisClient.buffers.set(bufferKey2(), [
      JSON.stringify(createGpsPayload(SECOND_TRIP_ID, '2026-06-03T10:01:00.000Z')),
    ]);
    prisma.gpsTrail.createMany
      .mockRejectedValueOnce(new Error('DB_INSERT_FAILED'))
      .mockResolvedValueOnce({ count: 1 });

    await expect(service.flushOnce()).resolves.toBe(1);

    expect(prisma.gpsTrail.createMany).toHaveBeenCalledTimes(2);
    expect(redisClient.buffers.has(processingKey())).toBe(true);
    expect(redisClient.buffers.has(processingKey2())).toBe(false);
  });

  it('retries existing processing key before draining new buffer', async () => {
    // Simulate stale processing key from previous failed flush
    redisClient.activeTripIds = [FIRST_TRIP_ID];
    const oldRows = [JSON.stringify(createGpsPayload(FIRST_TRIP_ID, '2026-06-03T09:00:00.000Z'))];
    redisClient.buffers.set(processingKey(), oldRows);

    // Also has new buffer data
    redisClient.buffers.set(bufferKey(), [
      JSON.stringify(createGpsPayload(FIRST_TRIP_ID, '2026-06-03T10:00:00.000Z')),
    ]);

    await expect(service.flushOnce()).resolves.toBe(1);

    // Processing key data was inserted (not new buffer)
    expect(prisma.gpsTrail.createMany).toHaveBeenCalledWith({
      data: [
        expect.objectContaining({ recordedAt: new Date('2026-06-03T09:00:00.000Z') }),
      ],
      skipDuplicates: true,
    });

    // Processing key deleted after success
    expect(redisClient.buffers.has(processingKey())).toBe(false);
    // New buffer still intact (not drained while processing existed)
    expect(redisClient.buffers.has(bufferKey())).toBe(true);
  });

  it('reports only rows inserted after database duplicate filtering', async () => {
    redisClient.activeTripIds = [FIRST_TRIP_ID];
    redisClient.buffers.set(bufferKey(), [
      JSON.stringify(createGpsPayload(FIRST_TRIP_ID, '2026-06-03T10:00:00.000Z')),
    ]);
    prisma.gpsTrail.createMany.mockResolvedValueOnce({ count: 0 });

    await expect(service.flushOnce()).resolves.toBe(0);

    expect(prisma.gpsTrail.createMany).toHaveBeenCalledWith(
      expect.objectContaining({ skipDuplicates: true }),
    );
    expect(redisClient.buffers.has(processingKey())).toBe(false);
  });

  it('recovers when all buffer rows are all-invalid', async () => {
    redisClient.activeTripIds = [FIRST_TRIP_ID];
    redisClient.buffers.set(bufferKey(), [
      JSON.stringify({ notAValid: 'payload' }),
    ]);

    await expect(service.flushOnce()).resolves.toBe(0);

    expect(prisma.gpsTrail.createMany).not.toHaveBeenCalled();
    // Processing key deleted even if all rows invalid
    expect(redisClient.buffers.has(processingKey())).toBe(false);
  });

  it('prunes idle trips after repeated empty cycles', async () => {
    redisClient.activeTripIds = [FIRST_TRIP_ID];

    for (let attempt = 0; attempt < GPS_BATCH_IDLE_CYCLES_BEFORE_PRUNE; attempt += 1) {
      await expect(service.flushOnce()).resolves.toBe(0);
    }

    expect(redisClient.activeTripIds).toEqual([]);
    expect(redisClient.idleCounts.has(idleKey())).toBe(false);
  });

  it('does not prune when a buffer appears during the idle check', async () => {
    redisClient.activeTripIds = [FIRST_TRIP_ID];
    redisClient.bufferAppearsDuringPrune = true;

    await expect(service.flushOnce()).resolves.toBe(0);

    expect(redisClient.activeTripIds).toEqual([FIRST_TRIP_ID]);
    expect(redisClient.idleCounts.has(idleKey())).toBe(false);
    expect(redisClient.buffers.has(bufferKey())).toBe(true);
  });
});

interface RedisClientMock {
  activeTripIds: string[];
  /** Buffer keys (including processing keys kept for testing) */
  buffers: Map<string, string[]>;
  idleCounts: Map<string, number>;
  bufferAppearsDuringPrune: boolean;
  smembers: jest.MockedFunction<(key: string) => Promise<string[]>>;
  lrange: jest.MockedFunction<(key: string, start: number, stop: number) => Promise<string[]>>;
  del: jest.MockedFunction<(key: string) => Promise<number>>;
  eval: jest.MockedFunction<(script: string, numKeys: number, ...args: string[]) => Promise<string[] | number[] | number>>;
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
    idleCounts: new Map<string, number>(),
    bufferAppearsDuringPrune: false,
    smembers: jest.fn(async (key: string) => {
      void key;
      return client.activeTripIds;
    }),
    lrange: jest.fn(async (key: string, _start: number, _stop: number) => {
      void _start;
      void _stop;
      return client.buffers.get(key) ?? [];
    }),
    del: jest.fn(async (key: string) => {
      client.buffers.delete(key);
      client.idleCounts.delete(key);
      return 1;
    }),
    eval: jest.fn(async (_script: string, _numKeys: number, ...args: string[]) => {
      if (_numKeys === 4) {
        const idleKeyArg = args[1] ?? '';
        const bufferKeyArg = args[2] ?? '';
        const processingKeyArg = args[3] ?? '';
        const tripId = args[4] ?? '';
        const threshold = Number(args[5] ?? '0');

        if (client.bufferAppearsDuringPrune) {
          client.buffers.set(bufferKeyArg, [
            JSON.stringify(createGpsPayload(tripId, '2026-06-03T10:05:00.000Z')),
          ]);
          client.bufferAppearsDuringPrune = false;
        }

        if (client.buffers.has(bufferKeyArg) || client.buffers.has(processingKeyArg)) {
          client.idleCounts.delete(idleKeyArg);
          return 0;
        }

        const idleCount = (client.idleCounts.get(idleKeyArg) ?? 0) + 1;
        client.idleCounts.set(idleKeyArg, idleCount);

        if (idleCount >= threshold) {
          client.activeTripIds = client.activeTripIds.filter((activeTripId) => activeTripId !== tripId);
          client.idleCounts.delete(idleKeyArg);
          return 1;
        }

        return 0;
      }

      // args[0] = buffer key, args[1] = processing key
      const bufferKey = args[0] ?? '';
      const procKey = args[1] ?? '';

      // Simulate Lua ATOMIC_RENAME_SCRIPT logic
      if (client.buffers.has(procKey)) {
        return [-1]; // PROCESSING_BUSY
      }
      if (!client.buffers.has(bufferKey)) {
        return [0]; // BUFFER_EMPTY
      }
      // RENAMENX: move buffer content to processing key
      const rows = client.buffers.get(bufferKey) ?? [];
      client.buffers.set(procKey, rows);
      client.buffers.delete(bufferKey);
      return rows;
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
