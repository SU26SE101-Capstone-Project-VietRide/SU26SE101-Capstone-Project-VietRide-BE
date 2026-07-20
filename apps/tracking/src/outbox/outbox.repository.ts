import { Injectable } from '@nestjs/common';
import { Prisma } from '../generated/tracking-prisma-client';
import { TrackingPrismaService } from '../prisma/tracking-prisma.service';
import {
  OUTBOX_LAST_ERROR_MAX_LENGTH,
  OUTBOX_MAX_RETRIES,
  OUTBOX_PUBLISHING_STALE_MS,
  OUTBOX_RETRY_BASE_DELAY_MS,
  OUTBOX_RETRY_MAX_DELAY_MS,
  OUTBOX_STALE_PUBLISHING_ERROR,
} from './outbox.constants';
import type { OutboxDlqQueryDto } from './outbox-dlq-query.dto';

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

export interface OutboxDlqReadItem {
  eventId: string;
  eventType: string;
  payload: unknown;
  retryCount: number;
  lastError: string;
  createdAt: Date;
  terminalAt: Date;
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
          retryCount: { lte: OUTBOX_MAX_RETRIES },
          OR: [{ nextRetryAt: null }, { nextRetryAt: { lte: new Date() } }],
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

  async readDlq(query: OutboxDlqQueryDto): Promise<OutboxDlqReadItem[]> {
    const descending = query.sortDir === 'desc';
    const cursorFilter =
      query.afterTerminalAt && query.afterId
        ? descending
          ? {
              OR: [
                { terminalAt: { lt: query.afterTerminalAt } },
                { terminalAt: query.afterTerminalAt, eventId: { lt: query.afterId } },
              ],
            }
          : {
              OR: [
                { terminalAt: { gt: query.afterTerminalAt } },
                { terminalAt: query.afterTerminalAt, eventId: { gt: query.afterId } },
              ],
            }
        : {};

    return this.prisma.outboxDlq.findMany({
      where: {
        ...(query.eventType ? { eventType: query.eventType } : {}),
        ...cursorFilter,
      },
      orderBy: [
        { terminalAt: descending ? 'desc' : 'asc' },
        { eventId: descending ? 'desc' : 'asc' },
      ],
      take: query.pageSize,
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
  }

  async recoverStalePublishingEvents(now: Date = new Date()): Promise<number> {
    const staleBefore = new Date(now.getTime() - OUTBOX_PUBLISHING_STALE_MS);
    const staleEvents = await this.prisma.outboxEvent.findMany({
      where: {
        status: 'PUBLISHING',
        updatedAt: {
          lt: staleBefore,
        },
      },
      select: { id: true },
    });

    const results = await Promise.all(
      staleEvents.map((event) => this.markFailed(event.id, OUTBOX_STALE_PUBLISHING_ERROR, now)),
    );
    return results.filter(Boolean).length;
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

  async markPublished(id: string, publishedAt: Date): Promise<boolean> {
    const result = await this.prisma.outboxEvent.updateMany({
      where: { id, status: 'PUBLISHING' },
      data: {
        status: 'PUBLISHED',
        publishedAt,
        lastError: null,
      },
    });

    return result.count === 1;
  }

  async markFailed(id: string, error: unknown, failedAt: Date = new Date()): Promise<boolean> {
    const lastError = formatLastError(error);

    return this.prisma.$transaction(async (tx) => {
      const event = await tx.outboxEvent.findUnique({ where: { id } });
      if (!event || event.status !== 'PUBLISHING') {
        return false;
      }

      const retryCount = event.retryCount + 1;
      const isTerminal = retryCount > OUTBOX_MAX_RETRIES;
      const nextRetryAt = isTerminal
        ? null
        : new Date(failedAt.getTime() + retryDelayMs(event.retryCount));
      const updated = await tx.outboxEvent.updateMany({
        where: {
          id,
          status: 'PUBLISHING',
          retryCount: event.retryCount,
        },
        data: {
          status: 'FAILED',
          retryCount,
          lastError,
          nextRetryAt,
        },
      });

      if (updated.count !== 1) {
        return false;
      }

      if (isTerminal) {
        await tx.outboxDlq.upsert({
          where: { eventId: event.id },
          create: {
            eventId: event.id,
            eventType: event.eventType,
            payload: event.payload === null ? Prisma.JsonNull : event.payload,
            retryCount,
            lastError,
            createdAt: event.createdAt,
            terminalAt: failedAt,
          },
          update: {},
        });
      }

      return true;
    });
  }
}

function retryDelayMs(currentRetryCount: number): number {
  return Math.min(
    Math.pow(2, currentRetryCount) * OUTBOX_RETRY_BASE_DELAY_MS,
    OUTBOX_RETRY_MAX_DELAY_MS,
  );
}

function formatLastError(error: unknown): string {
  const message = error instanceof Error ? error.message : String(error);
  return message.slice(0, OUTBOX_LAST_ERROR_MAX_LENGTH);
}
