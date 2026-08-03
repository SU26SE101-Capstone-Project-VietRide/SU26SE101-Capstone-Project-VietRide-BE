import {
  GoneException,
  Injectable,
  NotFoundException,
  UnauthorizedException,
} from '@nestjs/common';
import { timingSafeEqual } from 'node:crypto';
import { TripShareGrantRepository } from './trip-share-grant.repository';
import { TripShareRateLimiter } from './trip-share-rate-limiter';
import { TripShareTokenCodec } from './trip-share-token.codec';
import { TripShareTripSnapshotProvider } from './trip-share-trip-snapshot.provider';

const SHA_256_HEX_PATTERN = /^[a-f0-9]{64}$/;

export interface TripShareAccessContext {
  grantId: string;
  tripId: string;
  expiresAt: Date;
  status: 'IN_PROGRESS';
}

@Injectable()
export class TripShareAccessService {
  constructor(
    private readonly grants: TripShareGrantRepository,
    private readonly tokenCodec: TripShareTokenCodec,
    private readonly rateLimiter: TripShareRateLimiter,
    private readonly trips: TripShareTripSnapshotProvider,
  ) {}

  async authorize(rawToken: string | undefined, now: Date = new Date()): Promise<TripShareAccessContext> {
    if (!rawToken) this.invalidToken();
    await this.rateLimiter.consume('context', rawToken);

    const verified = this.tokenCodec.verify(rawToken);
    const grant = await this.grants.findById(verified.grantId);
    if (!grant || !this.hashesMatch(verified.tokenHash, grant.tokenHash)) this.invalidToken();
    if (grant.revokedAt) this.unavailable();
    if (grant.expiresAt.getTime() <= now.getTime()) {
      await this.grants.revokeGrantById(grant.id, 'EXPIRED', now);
      this.unavailable();
    }

    let snapshot: Awaited<ReturnType<TripShareTripSnapshotProvider['getTrip']>>;
    try {
      snapshot = await this.trips.getTrip(grant.tripId);
    } catch (error) {
      if (!(error instanceof NotFoundException)) throw error;
      await this.grants.revokeGrantById(grant.id, 'TRIP_TERMINATED', now);
      this.unavailable();
    }

    if (snapshot.status !== 'IN_PROGRESS') {
      await this.grants.revokeGrantById(grant.id, 'TRIP_TERMINATED', now);
      this.unavailable();
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
    return leftBuffer.byteLength === rightBuffer.byteLength && timingSafeEqual(leftBuffer, rightBuffer);
  }

  private invalidToken(): never {
    throw new UnauthorizedException({
      errorCode: 'TRACKING_SHARE_TOKEN_INVALID',
      detail: 'The trip share token is invalid',
    });
  }

  private unavailable(): never {
    throw new GoneException({
      errorCode: 'TRACKING_SHARE_LINK_UNAVAILABLE',
      detail: 'The trip share link is no longer available',
    });
  }
}
