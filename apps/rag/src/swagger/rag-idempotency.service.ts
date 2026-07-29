import { ConflictException, Injectable, UnprocessableEntityException } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import { createHash, randomUUID } from 'node:crypto';
import { RagPrismaService } from '../prisma/rag-prisma.service';

const PROCESSING_TTL_SECONDS = 120;
const RESPONSE_TTL_SECONDS = 86_400;
const FINGERPRINT_LENGTH = 64;
const V2_BARRIER_STATUS = 'V2_BARRIER';
const COMPLETE_SCRIPT = `
local current = redis.call('GET', KEYS[1])
if current ~= ARGV[1] then return 0 end
redis.call('SET', KEYS[2], ARGV[2], 'EX', ARGV[3])
redis.call('DEL', KEYS[1])
return 1
`;
const ABANDON_SCRIPT = `
local current = redis.call('GET', KEYS[1])
if current ~= ARGV[1] then return 0 end
redis.call('DEL', KEYS[1])
return 1
`;

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
  private readonly owners = new Map<
    string,
    {
      operationHash: string;
      operationId: string;
      fingerprint: string;
      completionStarted: boolean;
    }
  >();

  constructor(
    private readonly redis: RedisService,
    private readonly prisma: RagPrismaService,
  ) {}

  async begin(input: {
    operationId: string;
    userId: string;
    method: string;
    path: string;
    fingerprint: string;
  }): Promise<RagIdempotencyBeginResult> {
    const client = this.redis.getClient();
    const operationHash = this.hashOperationId(input.operationId);
    const responseKey = this.responseKey(operationHash);
    const processingKey = this.processingKey(operationHash);
    const completed = await client.get(responseKey);
    if (completed) return this.toReplay(completed, input.fingerprint);
    if (await client.get(this.legacyKey(input.operationId))) this.throwMismatch();

    const ownerToken = randomUUID();
    const barrier = await this.reserveLegacyBarrier(input, ownerToken);
    if (barrier === 'existing_v2') {
      const racedResponse = await client.get(responseKey);
      if (racedResponse) return this.toReplay(racedResponse, input.fingerprint);
      const activeLock = await client.get(processingKey);
      if (activeLock?.slice(0, FINGERPRINT_LENGTH) !== input.fingerprint) {
        if (activeLock) this.throwMismatch();
      }
      this.throwPending();
    }

    const lockValue = this.lockValue(input.fingerprint, ownerToken);
    let acquired: string | null;
    try {
      acquired = await client.set(processingKey, lockValue, 'EX', PROCESSING_TTL_SECONDS, 'NX');
    } catch (error) {
      await this.releaseLegacyBarrier(input.operationId, ownerToken);
      throw error;
    }
    if (acquired === 'OK') {
      this.owners.set(ownerToken, {
        operationHash,
        operationId: input.operationId,
        fingerprint: input.fingerprint,
        completionStarted: false,
      });
      return { state: 'acquired', operationId: input.operationId, ownerToken };
    }

    await this.releaseLegacyBarrier(input.operationId, ownerToken);
    const racedResponse = await client.get(responseKey);
    if (racedResponse) return this.toReplay(racedResponse, input.fingerprint);
    const activeLock = await client.get(processingKey);
    if (activeLock?.slice(0, FINGERPRINT_LENGTH) !== input.fingerprint) {
      if (activeLock) this.throwMismatch();
    }
    this.throwPending();
  }

  async complete(
    operationId: string,
    ownerToken: string,
    response: RagIdempotencyReplay,
  ): Promise<void> {
    const owner = this.requireOwner(operationId, ownerToken);
    owner.completionStarted = true;
    const barrier = await this.prisma.idempotencyOperation.updateMany({
      where: {
        operationId: owner.operationId,
        ownerToken,
        status: V2_BARRIER_STATUS,
      },
      data: { expiresAt: new Date(Date.now() + RESPONSE_TTL_SECONDS * 1_000) },
    });
    if (barrier.count !== 1) throw new Error('RAG_IDEMPOTENCY_LEGACY_BARRIER_NOT_OWNED');

    const result = await this.redis
      .getClient()
      .eval(
        COMPLETE_SCRIPT,
        2,
        this.processingKey(owner.operationHash),
        this.responseKey(owner.operationHash),
        this.lockValue(owner.fingerprint, ownerToken),
        JSON.stringify({ fingerprint: owner.fingerprint, ...response }),
        RESPONSE_TTL_SECONDS,
      );
    if (Number(result) !== 1) throw new Error('RAG_IDEMPOTENCY_LOCK_NOT_OWNED');
    this.owners.delete(ownerToken);
  }

  async abandon(operationId: string, ownerToken: string): Promise<void> {
    const owner = this.requireOwner(operationId, ownerToken);
    if (owner.completionStarted) {
      this.owners.delete(ownerToken);
      return;
    }
    await this.redis
      .getClient()
      .eval(
        ABANDON_SCRIPT,
        1,
        this.processingKey(owner.operationHash),
        this.lockValue(owner.fingerprint, ownerToken),
      );
    await this.releaseLegacyBarrier(owner.operationId, ownerToken);
    this.owners.delete(ownerToken);
  }

  private async reserveLegacyBarrier(
    input: {
      operationId: string;
      userId: string;
      method: string;
      path: string;
      fingerprint: string;
    },
    ownerToken: string,
  ): Promise<'acquired' | 'existing_v2'> {
    const now = new Date();
    const create = () =>
      this.prisma.idempotencyOperation.create({
        data: {
          operationId: input.operationId,
          userId: input.userId,
          method: input.method,
          path: input.path,
          fingerprint: input.fingerprint,
          ownerToken,
          status: V2_BARRIER_STATUS,
          expiresAt: new Date(now.getTime() + PROCESSING_TTL_SECONDS * 1_000),
        },
      });

    try {
      await create();
      return 'acquired';
    } catch (createError) {
      let existing = await this.prisma.idempotencyOperation.findUnique({
        where: { operationId: input.operationId },
      });
      if (!existing) throw createError;
      if (existing.expiresAt <= now) {
        const deleted = await this.prisma.idempotencyOperation.deleteMany({
          where: { operationId: input.operationId, expiresAt: { lte: now } },
        });
        if (deleted.count === 1) {
          await create();
          return 'acquired';
        }
        existing = await this.prisma.idempotencyOperation.findUnique({
          where: { operationId: input.operationId },
        });
        if (!existing) throw createError;
      }
      if (
        existing.status === V2_BARRIER_STATUS &&
        existing.userId === input.userId &&
        existing.fingerprint === input.fingerprint
      ) {
        return 'existing_v2';
      }
      this.throwMismatch();
    }
  }

  private async releaseLegacyBarrier(operationId: string, ownerToken: string): Promise<void> {
    await this.prisma.idempotencyOperation.deleteMany({
      where: { operationId, ownerToken, status: V2_BARRIER_STATUS },
    });
  }

  private toReplay(value: string, fingerprint: string): RagIdempotencyBeginResult {
    const parsed = JSON.parse(value) as Record<string, unknown>;
    if (parsed['fingerprint'] !== fingerprint) this.throwMismatch();
    if (
      typeof parsed['statusCode'] !== 'number' ||
      typeof parsed['body'] !== 'string' ||
      !parsed['headers'] ||
      typeof parsed['headers'] !== 'object' ||
      Array.isArray(parsed['headers'])
    ) {
      throw new Error('RAG_IDEMPOTENCY_RESPONSE_INVALID');
    }
    const headers = Object.fromEntries(
      Object.entries(parsed['headers']).filter(
        (entry): entry is [string, string] => typeof entry[1] === 'string',
      ),
    );
    return {
      state: 'replay',
      response: { statusCode: parsed['statusCode'], headers, body: parsed['body'] },
    };
  }

  private requireOwner(operationId: string, ownerToken: string) {
    const owner = this.owners.get(ownerToken);
    if (!owner || owner.operationHash !== this.hashOperationId(operationId)) {
      throw new Error('RAG_IDEMPOTENCY_LOCK_NOT_OWNED');
    }
    return owner;
  }

  private hashOperationId(operationId: string): string {
    return createHash('sha256')
      .update(operationId.toLowerCase(), 'utf8')
      .digest('hex')
      .toUpperCase();
  }

  private processingKey(operationHash: string): string {
    return `rag:idem:v2:processing:${operationHash}`;
  }

  private responseKey(operationHash: string): string {
    return `rag:idem:v2:response:${operationHash}`;
  }

  private legacyKey(operationId: string): string {
    return `rag:idem:${operationId}`;
  }

  private lockValue(fingerprint: string, ownerToken: string): string {
    return `${fingerprint}:${ownerToken}`;
  }

  private throwMismatch(): never {
    throw new UnprocessableEntityException({
      errorCode: 'IDEMPOTENCY_KEY_MISMATCH',
      detail: 'The Idempotency-Key was reused with a different request',
    });
  }

  private throwPending(): never {
    throw new ConflictException({
      errorCode: 'IDEMPOTENCY_REQUEST_PENDING',
      detail: 'The idempotent request is still being processed',
    });
  }
}
