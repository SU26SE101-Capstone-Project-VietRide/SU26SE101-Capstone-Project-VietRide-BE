import { ConflictException, HttpException, Inject, Injectable } from '@nestjs/common';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import { BookingOwnerAuthorizationProvider } from './booking-owner-authorization.provider';
import { TripShareGrantRepository } from './trip-share-grant.repository';
import { TripShareGrantService } from './trip-share-grant.service';
import { requireTripShareIdempotencyKey } from './trip-share-idempotency.helpers';
import type { TripShareIdempotencyOutcome } from './trip-share-idempotency.repository';
import { TripShareIdempotencyService } from './trip-share-idempotency.service';
import type {
  TripShareLinkResponseDto,
  TripShareRevokedResponseDto,
} from './trip-share-owner.dto';
import { TripShareTokenCodec } from './trip-share-token.codec';
import { TripShareTripSnapshotProvider } from './trip-share-trip-snapshot.provider';
import { TripShareRealtimePublisher } from './trip-share-realtime.publisher';
import { TripShareSubstitutionStateRepository } from './trip-share-substitution-state.repository';

const HTTP_CLIENT_ERROR_MIN = 400;
const HTTP_CLIENT_ERROR_MAX_EXCLUSIVE = 500;

@Injectable()
export class TripShareOwnerService {
  constructor(
    private readonly bookingOwner: BookingOwnerAuthorizationProvider,
    private readonly tripProvider: TripShareTripSnapshotProvider,
    private readonly grantService: TripShareGrantService,
    private readonly grantRepository: TripShareGrantRepository,
    private readonly idempotency: TripShareIdempotencyService,
    private readonly tokenCodec: TripShareTokenCodec,
    @Inject(ENV_TOKEN) private readonly env: Env,
    private readonly realtime: TripShareRealtimePublisher,
    private readonly substitutions: TripShareSubstitutionStateRepository,
  ) {}

  async ensureShareLink(
    userId: string,
    tripId: string,
    rawIdempotencyKey: string | undefined,
    path: string,
  ): Promise<TripShareLinkResponseDto> {
    const idempotencyKey = requireTripShareIdempotencyKey(rawIdempotencyKey);
    const fingerprint = this.fingerprint(userId, 'PUT', path, tripId);
    const begin = await this.idempotency.begin(idempotencyKey, fingerprint);
    if (begin.state === 'replay') return this.replayLinkOutcome(begin.outcome);

    try {
      await this.bookingOwner.requireBookingOwner(userId, tripId);
      await this.requireActiveTrip(tripId);
      const active = await this.grantService.ensureActive(tripId, userId);
      const secondSnapshot = await this.tripProvider.getTrip(tripId);
      if (secondSnapshot.status !== 'IN_PROGRESS') {
        await this.grantRepository.revokeGrantById(active.grant.id, 'CREATION_ROLLBACK', new Date());
        this.throwTripNotActive();
      }

      const outcome: TripShareIdempotencyOutcome = {
        kind: 'SHARE_GRANT',
        grantId: active.grant.id,
        expiresAt: active.grant.expiresAt.toISOString(),
      };
      await this.idempotency.complete(begin.ownerToken, outcome);
      return this.linkResponse(outcome.grantId, outcome.expiresAt);
    } catch (error) {
      await this.finishFailure(begin.ownerToken, error);
      throw error;
    }
  }

  async revokeShareLink(
    userId: string,
    tripId: string,
    rawIdempotencyKey: string | undefined,
    path: string,
  ): Promise<TripShareRevokedResponseDto> {
    const idempotencyKey = requireTripShareIdempotencyKey(rawIdempotencyKey);
    const fingerprint = this.fingerprint(userId, 'DELETE', path, tripId);
    const begin = await this.idempotency.begin(idempotencyKey, fingerprint);
    if (begin.state === 'replay') return this.replayRevokeOutcome(begin.outcome);

    try {
      const now = new Date();
      const currentTripId = await this.substitutions.resolveCurrentTripId(tripId);
      const active = await this.grantRepository.findActiveByOwnerTrip(currentTripId, userId, now);
      const revoked = active
        ? await this.grantRepository.revokeOwnActiveGrantById(
            active.id,
            currentTripId,
            userId,
            now,
          )
        : false;
      if (active && revoked) {
        void this.realtime.revokeGrant(active.id, 'REVOKED').catch(() => undefined);
      }
      const outcome = { kind: 'REVOKED', revoked: true } as const;
      await this.idempotency.complete(begin.ownerToken, outcome);
      return { revoked: true };
    } catch (error) {
      await this.finishFailure(begin.ownerToken, error);
      throw error;
    }
  }

  private async requireActiveTrip(tripId: string): Promise<void> {
    const snapshot = await this.tripProvider.getTrip(tripId);
    if (snapshot.status !== 'IN_PROGRESS') this.throwTripNotActive();
  }

  private fingerprint(userId: string, method: string, path: string, tripId: string): string {
    return TripShareIdempotencyService.fingerprint({ userId, method, path, tripId, body: null });
  }

  private async finishFailure(ownerToken: string, error: unknown): Promise<void> {
    const outcome = this.safeErrorOutcome(error);
    if (!outcome) {
      await this.idempotency.abandon(ownerToken);
      return;
    }
    try {
      await this.idempotency.complete(ownerToken, outcome);
    } catch (completionError) {
      await this.idempotency.abandon(ownerToken);
      throw completionError;
    }
  }

  private safeErrorOutcome(error: unknown): TripShareIdempotencyOutcome | null {
    if (!(error instanceof HttpException)) return null;
    const statusCode = error.getStatus();
    if (statusCode < HTTP_CLIENT_ERROR_MIN || statusCode >= HTTP_CLIENT_ERROR_MAX_EXCLUSIVE) return null;
    const response = error.getResponse();
    const record = typeof response === 'object' && response !== null
      ? response as Record<string, unknown>
      : {};
    return {
      kind: 'ERROR',
      statusCode,
      errorCode: typeof record['errorCode'] === 'string' ? record['errorCode'] : 'REQUEST_REJECTED',
      detail: typeof record['detail'] === 'string' ? record['detail'] : error.message,
    };
  }

  private replayLinkOutcome(outcome: TripShareIdempotencyOutcome): TripShareLinkResponseDto {
    if (outcome.kind === 'ERROR') this.throwStoredError(outcome);
    if (outcome.kind !== 'SHARE_GRANT') throw new Error('TRACKING_SHARE_IDEMPOTENCY_OUTCOME_MISMATCH');
    return this.linkResponse(outcome.grantId, outcome.expiresAt);
  }

  private replayRevokeOutcome(outcome: TripShareIdempotencyOutcome): TripShareRevokedResponseDto {
    if (outcome.kind === 'ERROR') this.throwStoredError(outcome);
    if (outcome.kind !== 'REVOKED') throw new Error('TRACKING_SHARE_IDEMPOTENCY_OUTCOME_MISMATCH');
    return { revoked: true };
  }

  private linkResponse(grantId: string, expiresAt: string): TripShareLinkResponseDto {
    const token = this.tokenCodec.create(grantId).token;
    const url = new URL(this.env.TRACKING_SHARE_PAGE_URL);
    url.hash = `token=${encodeURIComponent(token)}`;
    return { shareUrl: url.toString(), expiresAt };
  }

  private throwStoredError(outcome: Extract<TripShareIdempotencyOutcome, { kind: 'ERROR' }>): never {
    throw new HttpException(
      { errorCode: outcome.errorCode, detail: outcome.detail },
      outcome.statusCode,
    );
  }

  private throwTripNotActive(): never {
    throw new ConflictException({
      errorCode: 'TRACKING_TRIP_NOT_ACTIVE',
      detail: 'Trip must be IN_PROGRESS to create a share link',
    });
  }
}
