import { Injectable } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import { createHash } from 'node:crypto';

const PROCESSING_TTL_SECONDS = 120;
const RESULT_TTL_SECONDS = 86_400;
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

export type TripShareIdempotencyOutcome =
  | { kind: 'SHARE_GRANT'; grantId: string; expiresAt: string }
  | { kind: 'REVOKED'; revoked: true }
  | { kind: 'ERROR'; statusCode: number; errorCode: string; detail: string };

export interface TripShareIdempotencyStoredResult {
  fingerprint: string;
  outcome: TripShareIdempotencyOutcome;
}

@Injectable()
export class TripShareIdempotencyRepository {
  constructor(private readonly redis: RedisService) {}

  operationHash(idempotencyKey: string): string {
    return createHash('sha256').update(idempotencyKey.toLowerCase(), 'utf8').digest('hex');
  }

  processingKey(operationHash: string): string {
    return `tracking:idem:trip-share:processing:${operationHash}`;
  }

  resultKey(operationHash: string): string {
    return `tracking:idem:trip-share:result:${operationHash}`;
  }

  async readResult(operationHash: string): Promise<TripShareIdempotencyStoredResult | null> {
    const value = await this.redis.getClient().get(this.resultKey(operationHash));
    if (!value) return null;
    return this.parseStoredResult(value);
  }

  readLock(operationHash: string): Promise<string | null> {
    return this.redis.getClient().get(this.processingKey(operationHash));
  }

  async tryAcquire(operationHash: string, lockValue: string): Promise<boolean> {
    const result = await this.redis.getClient().set(
      this.processingKey(operationHash),
      lockValue,
      'EX',
      PROCESSING_TTL_SECONDS,
      'NX',
    );
    return result === 'OK';
  }

  async complete(
    operationHash: string,
    lockValue: string,
    fingerprint: string,
    outcome: TripShareIdempotencyOutcome,
  ): Promise<boolean> {
    const persisted = JSON.stringify({
      fingerprint,
      outcome: this.sanitizeOutcome(outcome),
    });
    const result = await this.redis.getClient().eval(
      COMPLETE_SCRIPT,
      2,
      this.processingKey(operationHash),
      this.resultKey(operationHash),
      lockValue,
      persisted,
      RESULT_TTL_SECONDS,
    );
    return Number(result) === 1;
  }

  async abandon(operationHash: string, lockValue: string): Promise<boolean> {
    const result = await this.redis.getClient().eval(
      ABANDON_SCRIPT,
      1,
      this.processingKey(operationHash),
      lockValue,
    );
    return Number(result) === 1;
  }

  private sanitizeOutcome(outcome: TripShareIdempotencyOutcome): TripShareIdempotencyOutcome {
    if (outcome.kind === 'SHARE_GRANT') {
      return { kind: 'SHARE_GRANT', grantId: outcome.grantId, expiresAt: outcome.expiresAt };
    }
    if (outcome.kind === 'REVOKED') return { kind: 'REVOKED', revoked: true };
    return {
      kind: 'ERROR',
      statusCode: outcome.statusCode,
      errorCode: outcome.errorCode,
      detail: outcome.detail,
    };
  }

  private parseStoredResult(value: string): TripShareIdempotencyStoredResult {
    const parsed = JSON.parse(value) as Record<string, unknown>;
    const fingerprint = parsed['fingerprint'];
    const outcome = parsed['outcome'];
    if (typeof fingerprint !== 'string' || !outcome || typeof outcome !== 'object') {
      throw new Error('TRACKING_SHARE_IDEMPOTENCY_RESULT_INVALID');
    }
    const record = outcome as Record<string, unknown>;
    if (
      record['kind'] === 'SHARE_GRANT' &&
      typeof record['grantId'] === 'string' &&
      typeof record['expiresAt'] === 'string'
    ) {
      return {
        fingerprint,
        outcome: {
          kind: 'SHARE_GRANT',
          grantId: record['grantId'],
          expiresAt: record['expiresAt'],
        },
      };
    }
    if (record['kind'] === 'REVOKED' && record['revoked'] === true) {
      return { fingerprint, outcome: { kind: 'REVOKED', revoked: true } };
    }
    if (
      record['kind'] === 'ERROR' &&
      typeof record['statusCode'] === 'number' &&
      record['statusCode'] >= 400 &&
      record['statusCode'] < 500 &&
      typeof record['errorCode'] === 'string' &&
      typeof record['detail'] === 'string'
    ) {
      return {
        fingerprint,
        outcome: {
          kind: 'ERROR',
          statusCode: record['statusCode'],
          errorCode: record['errorCode'],
          detail: record['detail'],
        },
      };
    }
    throw new Error('TRACKING_SHARE_IDEMPOTENCY_RESULT_INVALID');
  }
}
