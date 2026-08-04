import { Inject, Injectable } from '@nestjs/common';
import { randomUUID } from 'node:crypto';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import type { TripShareGrant } from '../generated/tracking-prisma-client';
import { TripShareGrantRepository } from './trip-share-grant.repository';
import { TripShareTokenCodec } from './trip-share-token.codec';

const TOKEN_VERSION = 1;
const MILLISECONDS_PER_SECOND = 1_000;

export interface ActiveTripShareGrant {
  grant: TripShareGrant;
  token: string;
}

@Injectable()
export class TripShareGrantService {
  constructor(
    private readonly repository: TripShareGrantRepository,
    private readonly codec: TripShareTokenCodec,
    @Inject(ENV_TOKEN) private readonly env: Env,
  ) {}

  async ensureActive(
    tripId: string,
    createdByUserId: string,
    now: Date = new Date(),
  ): Promise<ActiveTripShareGrant> {
    await this.repository.expireActiveForOwnerTrip(tripId, createdByUserId, now);
    const existing = await this.repository.findActiveByOwnerTrip(tripId, createdByUserId, now);
    if (existing) return this.withToken(existing);

    const id = randomUUID();
    const issued = this.codec.create(id);
    const expiresAt = new Date(
      now.getTime() + this.env.TRACKING_SHARE_TOKEN_TTL_SECONDS * MILLISECONDS_PER_SECOND,
    );

    try {
      const grant = await this.repository.create({
        id,
        tripId,
        createdByUserId,
        tokenHash: issued.tokenHash,
        tokenVersion: TOKEN_VERSION,
        expiresAt,
      });
      return { grant, token: issued.token };
    } catch (error) {
      if (!this.isUniqueConstraintViolation(error)) throw error;
      const winner = await this.repository.findActiveByOwnerTrip(tripId, createdByUserId, now);
      if (!winner) throw error;
      return this.withToken(winner);
    }
  }

  private withToken(grant: TripShareGrant): ActiveTripShareGrant {
    return { grant, token: this.codec.create(grant.id).token };
  }

  private isUniqueConstraintViolation(error: unknown): error is { code: 'P2002' } {
    return typeof error === 'object' && error !== null && 'code' in error && error.code === 'P2002';
  }
}
