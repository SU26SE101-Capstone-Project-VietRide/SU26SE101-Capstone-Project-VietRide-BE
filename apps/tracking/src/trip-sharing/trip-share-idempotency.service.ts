import { ConflictException, Injectable, UnprocessableEntityException } from '@nestjs/common';
import { createHash, randomUUID } from 'node:crypto';
import {
  TripShareIdempotencyRepository,
  type TripShareIdempotencyOutcome,
  type TripShareIdempotencyStoredResult,
} from './trip-share-idempotency.repository';

const FINGERPRINT_LENGTH = 64;

export interface TripShareFingerprintInput {
  userId: string;
  method: string;
  path: string;
  tripId: string;
  body: unknown;
}

export type TripShareIdempotencyBeginResult =
  | { state: 'acquired'; ownerToken: string }
  | { state: 'replay'; outcome: TripShareIdempotencyOutcome };

interface OwnedTripShareLock {
  operationHash: string;
  fingerprint: string;
  lockValue: string;
}

@Injectable()
export class TripShareIdempotencyService {
  private readonly ownedLocks = new Map<string, OwnedTripShareLock>();

  constructor(private readonly repository: TripShareIdempotencyRepository) {}

  static fingerprint(input: TripShareFingerprintInput): string {
    const fields = [
      input.userId,
      input.method.toUpperCase(),
      TripShareIdempotencyService.normalizePath(input.path),
      input.tripId,
      TripShareIdempotencyService.canonicalize(input.body),
    ];
    const lengthPrefixed = fields
      .map((field) => `${Buffer.byteLength(field, 'utf8')}:${field}`)
      .join('|');
    return createHash('sha256').update(lengthPrefixed, 'utf8').digest('hex');
  }

  async begin(idempotencyKey: string, fingerprint: string): Promise<TripShareIdempotencyBeginResult> {
    const operationHash = this.repository.operationHash(idempotencyKey);
    const completed = await this.repository.readResult(operationHash);
    if (completed) return this.toReplay(completed, fingerprint);

    const ownerToken = randomUUID();
    const lockValue = `${fingerprint}:${ownerToken}`;
    if (await this.repository.tryAcquire(operationHash, lockValue)) {
      this.ownedLocks.set(ownerToken, { operationHash, fingerprint, lockValue });
      return { state: 'acquired', ownerToken };
    }

    const racedResult = await this.repository.readResult(operationHash);
    if (racedResult) return this.toReplay(racedResult, fingerprint);
    const activeLock = await this.repository.readLock(operationHash);
    if (activeLock && activeLock.slice(0, FINGERPRINT_LENGTH) !== fingerprint) {
      this.throwMismatch();
    }
    this.throwPending();
  }

  async complete(
    ownerToken: string,
    outcome: TripShareIdempotencyOutcome,
  ): Promise<void> {
    const owned = this.requireOwned(ownerToken);
    const completed = await this.repository.complete(
      owned.operationHash,
      owned.lockValue,
      owned.fingerprint,
      outcome,
    );
    if (!completed) throw new Error('TRACKING_SHARE_IDEMPOTENCY_LOCK_NOT_OWNED');
    this.ownedLocks.delete(ownerToken);
  }

  async abandon(ownerToken: string): Promise<void> {
    const owned = this.ownedLocks.get(ownerToken);
    if (!owned) return;
    await this.repository.abandon(owned.operationHash, owned.lockValue);
    this.ownedLocks.delete(ownerToken);
  }

  private toReplay(
    stored: TripShareIdempotencyStoredResult,
    fingerprint: string,
  ): TripShareIdempotencyBeginResult {
    if (stored.fingerprint !== fingerprint) this.throwMismatch();
    return { state: 'replay', outcome: stored.outcome };
  }

  private requireOwned(ownerToken: string): OwnedTripShareLock {
    const owned = this.ownedLocks.get(ownerToken);
    if (!owned) throw new Error('TRACKING_SHARE_IDEMPOTENCY_LOCK_NOT_OWNED');
    return owned;
  }

  private static normalizePath(path: string): string {
    const withoutQuery = path.split('?', 1)[0] ?? '/';
    const collapsed = withoutQuery.replace(/\/{2,}/g, '/');
    const withLeadingSlash = collapsed.startsWith('/') ? collapsed : `/${collapsed}`;
    return withLeadingSlash.length > 1 ? withLeadingSlash.replace(/\/$/, '') : withLeadingSlash;
  }

  private static canonicalize(value: unknown): string {
    if (value === null) return 'null';
    if (Array.isArray(value)) {
      return `[${value.map((entry) => this.canonicalize(entry)).join(',')}]`;
    }
    if (typeof value === 'object') {
      const entries = Object.entries(value as Record<string, unknown>)
        .filter(([, entry]) => entry !== undefined)
        .sort(([left], [right]) => left.localeCompare(right))
        .map(([key, entry]) => `${JSON.stringify(key)}:${this.canonicalize(entry)}`);
      return `{${entries.join(',')}}`;
    }
    const serialized = JSON.stringify(value);
    return serialized ?? 'null';
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
