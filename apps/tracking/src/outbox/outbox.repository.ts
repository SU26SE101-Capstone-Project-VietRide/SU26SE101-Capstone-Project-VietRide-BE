import { Injectable } from '@nestjs/common';
import { TrackingPrismaService } from '../prisma/tracking-prisma.service';
import {
  OUTBOX_LAST_ERROR_MAX_LENGTH,
  OUTBOX_MAX_RETRIES,
  OUTBOX_PUBLISHING_STALE_MS,
  OUTBOX_RETRY_BASE_DELAY_MS,
  OUTBOX_RETRY_MAX_DELAY_MS,
  OUTBOX_STALE_PUBLISHING_ERROR,
} from './outbox.constants';

export type OutboxEventStatus = 'PENDING' | 'PUBLISHING' | 'PUBLISHED' | 'FAILED';

export interface OutboxEventRecord {
  id: string;
  eventType: string;
  payload: unknown;
  status: OutboxEventStatus;
  retryCount: number;
  lastError: string | null;
  createdAt: Date;
  updatedAt: Date;
  publishedAt: Date | null;
}

@Injectable()
export class OutboxRepository {
  constructor(private readonly prisma: TrackingPrismaService) {}

  async findPublishable(limit: number): Promise<OutboxEventRecord[]> {
    const rows = await this.prisma.outboxEvent.findMany({
      where: {
        status: 'PENDING',
      },
      orderBy: {
        createdAt: 'asc',
      },
      take: limit,
    });

    if (rows.length < limit) {
      const failedLimit = limit - rows.length;
      const failedRows = await this.prisma.outboxEvent.findMany({
        where: {
          status: 'FAILED',
          retryCount: { lt: OUTBOX_MAX_RETRIES },
          OR: [
            { nextRetryAt: null },
            { nextRetryAt: { lte: new Date() } },
          ],
        },
        orderBy: {
          createdAt: 'asc',
        },
        take: failedLimit,
      });
      rows.push(...failedRows);
    }

    return rows as OutboxEventRecord[];
  }

  async recoverStalePublishingEvents(now: Date = new Date()): Promise<number> {
    const staleBefore = new Date(now.getTime() - OUTBOX_PUBLISHING_STALE_MS);
    const result = await this.prisma.outboxEvent.updateMany({
      where: {
        status: 'PUBLISHING',
        updatedAt: {
          lt: staleBefore,
        },
      },
      data: {
        status: 'FAILED',
        retryCount: {
          increment: 1,
        },
        lastError: OUTBOX_STALE_PUBLISHING_ERROR,
        nextRetryAt: now,
      },
    });

    return result.count;
  }

  async markPublishing(id: string): Promise<boolean> {
    const result = await this.prisma.outboxEvent.updateMany({
      where: {
        id,
        status: {
          in: ['PENDING', 'FAILED'],
        },
      },
      data: {
        status: 'PUBLISHING',
        lastError: null,
      },
    });

    return result.count === 1;
  }

  async markPublished(id: string, publishedAt: Date): Promise<void> {
    await this.prisma.outboxEvent.update({
      where: { id },
      data: {
        status: 'PUBLISHED',
        publishedAt,
        lastError: null,
      },
    });
  }

  async markFailed(id: string, error: unknown, currentRetryCount: number): Promise<void> {
    const delayMs = Math.min(
      Math.pow(2, currentRetryCount) * OUTBOX_RETRY_BASE_DELAY_MS,
      OUTBOX_RETRY_MAX_DELAY_MS,
    );
    const nextRetryAt = new Date(Date.now() + delayMs);

    await this.prisma.outboxEvent.update({
      where: { id },
      data: {
        status: 'FAILED',
        retryCount: {
          increment: 1,
        },
        lastError: formatLastError(error),
        nextRetryAt,
      },
    });
  }
}

function formatLastError(error: unknown): string {
  const message = error instanceof Error ? error.message : String(error);
  return message.slice(0, OUTBOX_LAST_ERROR_MAX_LENGTH);
}
