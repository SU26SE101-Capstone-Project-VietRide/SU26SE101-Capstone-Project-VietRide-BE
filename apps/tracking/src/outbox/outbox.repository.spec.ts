import {
  OUTBOX_MAX_RETRIES,
  OUTBOX_PUBLISHING_STALE_MS,
  OUTBOX_STALE_PUBLISHING_ERROR,
} from './outbox.constants';
import { OutboxRepository } from './outbox.repository';

const EVENT_ID = '11111111-1111-4111-8111-111111111111';
const NOW = new Date('2026-06-19T12:00:00.000Z');

interface PrismaMock {
  outboxEvent: {
    findMany: jest.Mock;
    findUnique: jest.Mock;
    updateMany: jest.Mock;
    update: jest.Mock;
  };
  outboxDlq: {
    findMany: jest.Mock;
    upsert: jest.Mock;
  };
  $transaction: jest.Mock;
}

const createPrismaMock = (): PrismaMock => {
  const prisma = {
    outboxEvent: {
      findMany: jest.fn(),
      findUnique: jest.fn(),
      updateMany: jest.fn(),
      update: jest.fn(),
    },
    outboxDlq: {
      findMany: jest.fn(),
      upsert: jest.fn(),
    },
    $transaction: jest.fn(),
  } as PrismaMock;

  prisma.$transaction.mockImplementation(async (callback: (tx: PrismaMock) => unknown) =>
    callback(prisma),
  );
  return prisma;
};

describe('OutboxRepository', () => {
  let prisma: PrismaMock;
  let repository: OutboxRepository;

  beforeEach(() => {
    jest.useFakeTimers({ now: NOW });
    prisma = createPrismaMock();
    prisma.outboxEvent.findMany.mockResolvedValue([]);
    repository = new OutboxRepository(prisma as unknown as never);
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  describe('findPublishable', () => {
    it('fetches PENDING rows first; falls through to FAILED if under limit', async () => {
      prisma.outboxEvent.findMany.mockResolvedValueOnce([
        { id: EVENT_ID, status: 'PENDING', retryCount: 0 },
      ]);

      await repository.findPublishable(25);

      expect(prisma.outboxEvent.findMany).toHaveBeenNthCalledWith(1, {
        where: { status: 'PENDING' },
        orderBy: { createdAt: 'asc' },
        take: 25,
      });
    });

    it('includes FAILED rows with nextRetryAt = null', async () => {
      prisma.outboxEvent.findMany
        .mockResolvedValueOnce([])
        .mockResolvedValueOnce([
          { id: EVENT_ID, status: 'FAILED', retryCount: 1, nextRetryAt: null },
        ]);

      const result = await repository.findPublishable(25);

      expect(prisma.outboxEvent.findMany).toHaveBeenNthCalledWith(2, {
        where: {
          status: 'FAILED',
          retryCount: { lte: OUTBOX_MAX_RETRIES },
          OR: [{ nextRetryAt: null }, { nextRetryAt: { lte: NOW } }],
        },
        orderBy: { createdAt: 'asc' },
        take: 25,
      });
      expect(result).toHaveLength(1);
    });

    it('includes FAILED rows with past-due nextRetryAt', async () => {
      prisma.outboxEvent.findMany
        .mockResolvedValueOnce([])
        .mockResolvedValueOnce([
          {
            id: EVENT_ID,
            status: 'FAILED',
            retryCount: 2,
            nextRetryAt: new Date(NOW.getTime() - 60_000),
          },
        ]);

      const result = await repository.findPublishable(25);

      expect(result).toHaveLength(1);
    });

    it('excludes FAILED rows with future nextRetryAt', async () => {
      const result = await repository.findPublishable(25);

      expect(result).toHaveLength(0);
    });

    it('excludes FAILED rows that exceed max retries', async () => {
      const result = await repository.findPublishable(25);

      expect(result).toHaveLength(0);
    });
  });

  describe('markFailed', () => {
    it('keeps an event retryable through retry_count = 5', async () => {
      prisma.outboxEvent.findUnique.mockResolvedValueOnce(createPublishingEvent(4));
      prisma.outboxEvent.updateMany.mockResolvedValueOnce({ count: 1 });

      await expect(repository.markFailed(EVENT_ID, new Error('timeout'), NOW)).resolves.toBe(true);

      expect(prisma.outboxEvent.updateMany).toHaveBeenCalledWith({
        where: { id: EVENT_ID, status: 'PUBLISHING', retryCount: 4 },
        data: {
          status: 'FAILED',
          retryCount: 5,
          lastError: 'timeout',
          nextRetryAt: new Date(NOW.getTime() + 32_000),
        },
      });
      expect(prisma.outboxDlq.upsert).not.toHaveBeenCalled();
    });

    it('moves the sixth failed publish to DLQ exactly once and preserves source payload', async () => {
      const publishingEvent = createPublishingEvent(5);
      prisma.outboxEvent.findUnique
        .mockResolvedValueOnce(publishingEvent)
        .mockResolvedValueOnce({ ...publishingEvent, status: 'FAILED', retryCount: 6 });
      prisma.outboxEvent.updateMany.mockResolvedValueOnce({ count: 1 });

      await expect(
        repository.markFailed(EVENT_ID, new Error('broker unavailable'), NOW),
      ).resolves.toBe(true);
      await expect(repository.markFailed(EVENT_ID, new Error('duplicate tick'), NOW)).resolves.toBe(
        false,
      );

      expect(prisma.outboxEvent.updateMany).toHaveBeenCalledWith({
        where: { id: EVENT_ID, status: 'PUBLISHING', retryCount: 5 },
        data: {
          status: 'FAILED',
          retryCount: 6,
          lastError: 'broker unavailable',
          nextRetryAt: null,
        },
      });
      expect(prisma.outboxDlq.upsert).toHaveBeenCalledTimes(1);
      expect(prisma.outboxDlq.upsert).toHaveBeenCalledWith({
        where: { eventId: EVENT_ID },
        create: {
          eventId: EVENT_ID,
          eventType: 'TripDelayed',
          payload: { tripId: EVENT_ID },
          retryCount: 6,
          lastError: 'broker unavailable',
          createdAt: publishingEvent.createdAt,
          terminalAt: NOW,
        },
        update: {},
      });
    });
  });

  describe('readDlq', () => {
    it('filters by event type and applies the descending compound cursor', async () => {
      prisma.outboxDlq.findMany.mockResolvedValueOnce([]);
      const terminalAt = new Date('2026-07-18T10:05:00.000Z');
      const cursorEventId = '33333333-3333-4333-8333-333333333333';

      await repository.readDlq({
        eventType: 'TripDelayed',
        pageSize: 25,
        afterTerminalAt: terminalAt,
        afterId: cursorEventId,
        sortDir: 'desc',
      });

      expect(prisma.outboxDlq.findMany).toHaveBeenCalledWith({
        where: {
          eventType: 'TripDelayed',
          OR: [
            { terminalAt: { lt: terminalAt } },
            { terminalAt, eventId: { lt: cursorEventId } },
          ],
        },
        orderBy: [{ terminalAt: 'desc' }, { eventId: 'desc' }],
        take: 25,
        select: {
          eventId: true,
          eventType: true,
          payload: true,
          retryCount: true,
          lastError: true,
          createdAt: true,
          terminalAt: true,
        },
      });
    });

    it('uses ascending ordering without cursor filters when requested', async () => {
      prisma.outboxDlq.findMany.mockResolvedValueOnce([]);

      await repository.readDlq({ pageSize: 10, sortDir: 'asc' });

      expect(prisma.outboxDlq.findMany).toHaveBeenCalledWith({
        where: {},
        orderBy: [{ terminalAt: 'asc' }, { eventId: 'asc' }],
        take: 10,
        select: {
          eventId: true,
          eventType: true,
          payload: true,
          retryCount: true,
          lastError: true,
          createdAt: true,
          terminalAt: true,
        },
      });
    });
  });

  describe('recoverStalePublishingEvents', () => {
    it('marks stale PUBLISHING rows as FAILED and ready for retry', async () => {
      prisma.outboxEvent.findMany.mockResolvedValueOnce([{ id: EVENT_ID }]);
      prisma.outboxEvent.findUnique.mockResolvedValueOnce(createPublishingEvent(0));
      prisma.outboxEvent.updateMany.mockResolvedValueOnce({ count: 1 });

      await expect(repository.recoverStalePublishingEvents(NOW)).resolves.toBe(1);

      expect(prisma.outboxEvent.findMany).toHaveBeenCalledWith({
        where: {
          status: 'PUBLISHING',
          updatedAt: {
            lt: new Date(NOW.getTime() - OUTBOX_PUBLISHING_STALE_MS),
          },
        },
        select: { id: true },
      });
      expect(prisma.outboxEvent.updateMany).toHaveBeenCalledWith({
        where: { id: EVENT_ID, status: 'PUBLISHING', retryCount: 0 },
        data: expect.objectContaining({
          status: 'FAILED',
          retryCount: 1,
          lastError: OUTBOX_STALE_PUBLISHING_ERROR,
          nextRetryAt: new Date(NOW.getTime() + 2_000),
        }),
      });
    });

    it('does not recover fresh PUBLISHING rows before the stale cutoff', async () => {
      prisma.outboxEvent.findMany.mockResolvedValueOnce([]);

      await expect(repository.recoverStalePublishingEvents(NOW)).resolves.toBe(0);

      expect(prisma.outboxEvent.findMany).toHaveBeenCalledWith(
        expect.objectContaining({
          where: expect.objectContaining({
            status: 'PUBLISHING',
            updatedAt: {
              lt: new Date(NOW.getTime() - OUTBOX_PUBLISHING_STALE_MS),
            },
          }),
        }),
      );
    });
  });
});

function createPublishingEvent(retryCount: number) {
  return {
    id: EVENT_ID,
    eventType: 'TripDelayed',
    payload: { tripId: EVENT_ID },
    status: 'PUBLISHING',
    retryCount,
    lastError: null,
    nextRetryAt: null,
    createdAt: new Date('2026-06-19T11:00:00.000Z'),
    updatedAt: NOW,
    publishedAt: null,
  };
}
