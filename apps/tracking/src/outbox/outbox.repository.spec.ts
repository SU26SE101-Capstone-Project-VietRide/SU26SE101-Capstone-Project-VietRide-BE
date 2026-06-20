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
    count: jest.Mock;
    updateMany: jest.Mock;
    update: jest.Mock;
  };
}

const createPrismaMock = (): PrismaMock => ({
  outboxEvent: {
    findMany: jest.fn(),
    count: jest.fn(),
    updateMany: jest.fn(),
    update: jest.fn(),
  },
});

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
      prisma.outboxEvent.findMany
        .mockResolvedValueOnce([
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
          retryCount: { lt: OUTBOX_MAX_RETRIES },
          OR: [
            { nextRetryAt: null },
            { nextRetryAt: { lte: NOW } },
          ],
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
          { id: EVENT_ID, status: 'FAILED', retryCount: 2, nextRetryAt: new Date(NOW.getTime() - 60_000) },
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
    it('sets nextRetryAt = now + 2s when currentRetryCount = 0', async () => {
      await repository.markFailed(EVENT_ID, new Error('timeout'), 0);

      const expectedNextRetry = new Date(NOW.getTime() + 2000);
      expect(prisma.outboxEvent.update).toHaveBeenCalledWith({
        where: { id: EVENT_ID },
        data: expect.objectContaining({ nextRetryAt: expectedNextRetry }),
      });
    });

    it('sets nextRetryAt = now + 4s when currentRetryCount = 1', async () => {
      await repository.markFailed(EVENT_ID, new Error('timeout'), 1);

      const expectedNextRetry = new Date(NOW.getTime() + 4000);
      expect(prisma.outboxEvent.update).toHaveBeenCalledWith({
        where: { id: EVENT_ID },
        data: expect.objectContaining({ nextRetryAt: expectedNextRetry }),
      });
    });

    it('caps nextRetryAt at 1h when exponential delay exceeds max', async () => {
      await repository.markFailed(EVENT_ID, new Error('timeout'), 11);

      const expectedNextRetry = new Date(NOW.getTime() + 3_600_000);
      expect(prisma.outboxEvent.update).toHaveBeenCalledWith({
        where: { id: EVENT_ID },
        data: expect.objectContaining({ nextRetryAt: expectedNextRetry }),
      });
    });
  });

  describe('recoverStalePublishingEvents', () => {
    it('marks stale PUBLISHING rows as FAILED and ready for retry', async () => {
      prisma.outboxEvent.updateMany.mockResolvedValueOnce({ count: 1 });

      await expect(repository.recoverStalePublishingEvents(NOW)).resolves.toBe(1);

      expect(prisma.outboxEvent.updateMany).toHaveBeenCalledWith({
        where: {
          status: 'PUBLISHING',
          updatedAt: {
            lt: new Date(NOW.getTime() - OUTBOX_PUBLISHING_STALE_MS),
          },
        },
        data: {
          status: 'FAILED',
          retryCount: {
            increment: 1,
          },
          lastError: OUTBOX_STALE_PUBLISHING_ERROR,
          nextRetryAt: NOW,
        },
      });
    });

    it('does not recover fresh PUBLISHING rows before the stale cutoff', async () => {
      prisma.outboxEvent.updateMany.mockResolvedValueOnce({ count: 0 });

      await expect(repository.recoverStalePublishingEvents(NOW)).resolves.toBe(0);

      expect(prisma.outboxEvent.updateMany).toHaveBeenCalledWith(
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
