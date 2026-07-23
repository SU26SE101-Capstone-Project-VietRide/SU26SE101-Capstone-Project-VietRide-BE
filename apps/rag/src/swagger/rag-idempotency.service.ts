import {
  ConflictException,
  Injectable,
  UnprocessableEntityException,
} from '@nestjs/common';
import { randomUUID } from 'node:crypto';
import { RagPrismaService } from '../prisma/rag-prisma.service';

const OPERATION_TTL_MS = 86_400_000;

export interface RagIdempotencyReplay {
  statusCode: number;
  headers: Record<string, string>;
  body: string;
}

export type RagIdempotencyBeginResult =
  | { state: 'acquired'; operationId: string; ownerToken: string }
  | { state: 'replay'; response: RagIdempotencyReplay };

@Injectable()
export class RagIdempotencyService {
  constructor(private readonly prisma: RagPrismaService) {}

  async begin(input: {
    operationId: string;
    userId: string;
    method: string;
    path: string;
    fingerprint: string;
  }): Promise<RagIdempotencyBeginResult> {
    const ownerToken = randomUUID();
    const now = new Date();
    try {
      await this.prisma.idempotencyOperation.create({
        data: {
          operationId: input.operationId,
          userId: input.userId,
          method: input.method,
          path: input.path,
          fingerprint: input.fingerprint,
          ownerToken,
          status: 'PROCESSING',
          expiresAt: new Date(now.getTime() + OPERATION_TTL_MS),
        },
      });
      return { state: 'acquired', operationId: input.operationId, ownerToken };
    } catch {
      const existing = await this.prisma.idempotencyOperation.findUnique({
        where: { operationId: input.operationId },
      });
      if (!existing) throw new Error('RAG_IDEMPOTENCY_OPERATION_LOOKUP_FAILED');
      if (existing.userId !== input.userId || existing.fingerprint !== input.fingerprint) {
        throw new UnprocessableEntityException({
          errorCode: 'IDEMPOTENCY_KEY_MISMATCH',
          detail: 'The Idempotency-Key was reused with a different request',
        });
      }
      if (existing.status === 'COMPLETED' && existing.responseStatus !== null) {
        return {
          state: 'replay',
          response: {
            statusCode: existing.responseStatus,
            headers: this.parseHeaders(existing.responseHeaders),
            body: existing.responseBody ?? '',
          },
        };
      }
      if (existing.expiresAt <= now) {
        await this.prisma.idempotencyOperation.update({
          where: { operationId: input.operationId },
          data: {
            ownerToken,
            status: 'PROCESSING',
            expiresAt: new Date(now.getTime() + OPERATION_TTL_MS),
          },
        });
        return { state: 'acquired', operationId: input.operationId, ownerToken };
      }
      throw new ConflictException({
        errorCode: 'IDEMPOTENCY_REQUEST_PENDING',
        detail: 'The idempotent request is still being processed',
      });
    }
  }

  async complete(
    operationId: string,
    ownerToken: string,
    response: RagIdempotencyReplay,
  ): Promise<void> {
    const updated = await this.prisma.idempotencyOperation.updateMany({
      where: { operationId, ownerToken, status: 'PROCESSING' },
      data: {
        status: 'COMPLETED',
        responseStatus: response.statusCode,
        responseHeaders: response.headers,
        responseBody: response.body,
      },
    });
    if (updated.count !== 1) throw new Error('RAG_IDEMPOTENCY_LOCK_NOT_OWNED');
  }

  async abandon(operationId: string, ownerToken: string): Promise<void> {
    await this.prisma.idempotencyOperation.deleteMany({
      where: { operationId, ownerToken, status: 'PROCESSING' },
    });
  }

  private parseHeaders(value: unknown): Record<string, string> {
    if (!value || typeof value !== 'object' || Array.isArray(value)) return {};
    return Object.fromEntries(
      Object.entries(value).filter((entry): entry is [string, string] =>
        typeof entry[1] === 'string',
      ),
    );
  }
}
