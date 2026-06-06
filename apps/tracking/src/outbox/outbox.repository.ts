import { Injectable } from '@nestjs/common';
import { TrackingPrismaService } from '../prisma/tracking-prisma.service';
import { OUTBOX_LAST_ERROR_MAX_LENGTH } from './outbox.constants';

export type OutboxEventStatus = 'PENDING' | 'PUBLISHING' | 'PUBLISHED' | 'FAILED';

export interface OutboxEventRecord {
  id: string;
  eventType: string;
  payload: unknown;
  status: OutboxEventStatus;
  retryCount: number;
  lastError: string | null;
  createdAt: Date;
  publishedAt: Date | null;
}

@Injectable()
export class OutboxRepository {
  constructor(private readonly prisma: TrackingPrismaService) {}

  async findPublishable(limit: number): Promise<OutboxEventRecord[]> {
    const rows = await this.prisma.outboxEvent.findMany({
      where: {
        status: {
          in: ['PENDING', 'FAILED'],
        },
      },
      orderBy: {
        createdAt: 'asc',
      },
      take: limit,
    });

    return rows as OutboxEventRecord[];
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

  async markFailed(id: string, error: unknown): Promise<void> {
    await this.prisma.outboxEvent.update({
      where: { id },
      data: {
        status: 'FAILED',
        retryCount: {
          increment: 1,
        },
        lastError: formatLastError(error),
      },
    });
  }
}

function formatLastError(error: unknown): string {
  const message = error instanceof Error ? error.message : String(error);
  return message.slice(0, OUTBOX_LAST_ERROR_MAX_LENGTH);
}
