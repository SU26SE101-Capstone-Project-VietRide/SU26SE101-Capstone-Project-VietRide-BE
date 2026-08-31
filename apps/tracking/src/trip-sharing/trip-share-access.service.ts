import {
  GoneException,
  Injectable,
  NotFoundException,
  UnauthorizedException,
} from '@nestjs/common';
import { timingSafeEqual } from 'node:crypto';
import { TripShareGrantRepository } from './trip-share-grant.repository';
import { TripShareRateLimiter, type TripShareRateLimitSurface } from './trip-share-rate-limiter';
import { TripShareTokenCodec } from './trip-share-token.codec';
import { TripShareTripSnapshotProvider } from './trip-share-trip-snapshot.provider';
import { TripShareSubstitutionStateRepository } from './trip-share-substitution-state.repository';

const SHA_256_HEX_PATTERN = /^[a-f0-9]{64}$/;

export interface TripShareAccessContext {
  grantId: string;
  tripId: string;
  expiresAt: Date;
  status: 'IN_PROGRESS' | 'VEHICLE_REPLACEMENT_PENDING';
}

@Injectable()
export class TripShareAccessService {
  constructor(
    private readonly grants: TripShareGrantRepository,
    private readonly tokenCodec: TripShareTokenCodec,
    private readonly rateLimiter: TripShareRateLimiter,
    private readonly trips: TripShareTripSnapshotProvider,
    private readonly substitutions: TripShareSubstitutionStateRepository,
  ) {}

  async authorize(
    rawToken: string | undefined,
    now: Date = new Date(),
  ): Promise<TripShareAccessContext> {
    return this.authorizeWithSurface(rawToken, 'context', now);
  }

  async authorizeSocket(
    rawToken: string | undefined,
    now: Date = new Date(),
  ): Promise<TripShareAccessContext> {
    return this.authorizeWithSurface(rawToken, 'socket', now);
  }

  async revalidate(
    rawToken: string | undefined,
    now: Date = new Date(),
  ): Promise<TripShareAccessContext> {
    if (!rawToken) this.invalidToken();
    return this.validate(rawToken, now);
  }

  private async authorizeWithSurface(
    rawToken: string | undefined,
    surface: TripShareRateLimitSurface,
    now: Date,
  ): Promise<TripShareAccessContext> {
    if (!rawToken) this.invalidToken();
    await this.rateLimiter.consume(surface, rawToken);
    return this.validate(rawToken, now);
  }

  private async validate(rawToken: string, now: Date): Promise<TripShareAccessContext> {
    const verified = this.tokenCodec.verify(rawToken);
    const grant = await this.grants.findById(verified.grantId);
    if (!grant || !this.hashesMatch(verified.tokenHash, grant.tokenHash)) this.invalidToken();
    if (grant.revokedAt) {
      const reason =
        grant.revokeReason === 'TRIP_TERMINATED' || grant.revokeReason === 'CREATION_ROLLBACK'
          ? 'TRIP_ENDED'
          : grant.revokeReason === 'EXPIRED'
            ? 'EXPIRED'
            : 'REVOKED';
      this.unavailable(reason);
    }
    if (grant.expiresAt.getTime() <= now.getTime()) {
      await this.grants.revokeGrantById(grant.id, 'EXPIRED', now);
      this.unavailable('EXPIRED');
    }

    let snapshot: Awaited<ReturnType<TripShareTripSnapshotProvider['getTrip']>>;
    try {
      snapshot = await this.trips.getTrip(grant.tripId);
    } catch (error) {
      if (!(error instanceof NotFoundException)) throw error;
      await this.grants.revokeGrantById(grant.id, 'TRIP_TERMINATED', now);
      this.unavailable('TRIP_ENDED');
    }

    const status = snapshot.status.toUpperCase();
    // Share grants cannot be created for BOARDING Trips. Reaching this state with an active grant
    // therefore means the DB transfer committed and the Redis alias may still be catching up on a
    // retry. Keep the capability alive instead of revoking it in that transient window.
    if (status === 'BOARDING') {
      return {
        grantId: grant.id,
        tripId: grant.tripId,
        expiresAt: grant.expiresAt,
        status: 'VEHICLE_REPLACEMENT_PENDING',
      };
    }
    if (status === 'DISRUPTED' && await this.substitutions.isPending(grant.tripId)) {
      return {
        grantId: grant.id,
        tripId: grant.tripId,
        expiresAt: grant.expiresAt,
        status: 'VEHICLE_REPLACEMENT_PENDING',
      };
    }
    if (status !== 'IN_PROGRESS') {
      await this.grants.revokeGrantById(grant.id, 'TRIP_TERMINATED', now);
      this.unavailable('TRIP_ENDED');
    }

    return {
      grantId: grant.id,
      tripId: grant.tripId,
      expiresAt: grant.expiresAt,
      status: 'IN_PROGRESS',
    };
  }

  private hashesMatch(left: string, right: string): boolean {
    const normalizedLeft = left.toLowerCase();
    const normalizedRight = right.toLowerCase();
    if (!SHA_256_HEX_PATTERN.test(normalizedLeft) || !SHA_256_HEX_PATTERN.test(normalizedRight)) {
      return false;
    }
    const leftBuffer = Buffer.from(normalizedLeft, 'ascii');
    const rightBuffer = Buffer.from(normalizedRight, 'ascii');
    return (
      leftBuffer.byteLength === rightBuffer.byteLength && timingSafeEqual(leftBuffer, rightBuffer)
    );
  }

  private invalidToken(): never {
    throw new UnauthorizedException({
      errorCode: 'TRACKING_SHARE_TOKEN_INVALID',
      detail: 'The trip share token is invalid',
    });
  }

  private unavailable(reason: 'REVOKED' | 'EXPIRED' | 'TRIP_ENDED'): never {
    throw new GoneException(
      {
        errorCode: 'TRACKING_SHARE_LINK_UNAVAILABLE',
        detail: 'The trip share link is no longer available',
      },
      { cause: reason },
    );
  }
}
