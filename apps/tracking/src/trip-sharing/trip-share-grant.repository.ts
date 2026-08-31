import { Injectable } from '@nestjs/common';
import type {
  TripShareGrant,
  TripShareGrantRevokeReason,
} from '../generated/tracking-prisma-client';
import { TrackingPrismaService } from '../prisma/tracking-prisma.service';

export interface CreateTripShareGrantInput {
  id: string;
  tripId: string;
  createdByUserId: string;
  tokenHash: string;
  tokenVersion: number;
  expiresAt: Date;
}

@Injectable()
export class TripShareGrantRepository {
  constructor(private readonly prisma: TrackingPrismaService) {}

  async expireActiveForOwnerTrip(tripId: string, createdByUserId: string, now: Date): Promise<number> {
    const result = await this.prisma.tripShareGrant.updateMany({
      where: { tripId, createdByUserId, revokedAt: null, expiresAt: { lte: now } },
      data: { revokedAt: now, revokeReason: 'EXPIRED' },
    });
    return result.count;
  }

  findActiveByOwnerTrip(
    tripId: string,
    createdByUserId: string,
    now: Date,
  ): Promise<TripShareGrant | null> {
    return this.prisma.tripShareGrant.findFirst({
      where: { tripId, createdByUserId, revokedAt: null, expiresAt: { gt: now } },
    });
  }

  findById(id: string): Promise<TripShareGrant | null> {
    return this.prisma.tripShareGrant.findUnique({ where: { id } });
  }

  async hasActiveForTrip(tripId: string, now: Date): Promise<boolean> {
    const count = await this.prisma.tripShareGrant.count({
      where: { tripId, revokedAt: null, expiresAt: { gt: now } },
    });
    return count > 0;
  }

  create(input: CreateTripShareGrantInput): Promise<TripShareGrant> {
    return this.prisma.tripShareGrant.create({ data: input });
  }

  async revokeOwnActiveGrant(tripId: string, createdByUserId: string, now: Date): Promise<number> {
    const result = await this.prisma.tripShareGrant.updateMany({
      where: { tripId, createdByUserId, revokedAt: null },
      data: { revokedAt: now, revokeReason: 'USER_REVOKED' },
    });
    return result.count;
  }

  async revokeOwnActiveGrantById(
    id: string,
    tripId: string,
    createdByUserId: string,
    now: Date,
  ): Promise<boolean> {
    const result = await this.prisma.tripShareGrant.updateMany({
      where: { id, tripId, createdByUserId, revokedAt: null },
      data: { revokedAt: now, revokeReason: 'USER_REVOKED' },
    });
    return result.count === 1;
  }

  async revokeGrantById(
    id: string,
    reason: TripShareGrantRevokeReason,
    now: Date,
  ): Promise<number> {
    const result = await this.prisma.tripShareGrant.updateMany({
      where: { id, revokedAt: null },
      data: { revokedAt: now, revokeReason: reason },
    });
    return result.count;
  }

  async revokeAllActiveForTrip(tripId: string, now: Date): Promise<number> {
    const result = await this.prisma.tripShareGrant.updateMany({
      where: { tripId, revokedAt: null },
      data: { revokedAt: now, revokeReason: 'TRIP_TERMINATED' },
    });
    return result.count;
  }

  transferActiveGrants(oldTripId: string, newTripId: string, now: Date): Promise<number> {
    return this.prisma.$transaction(async (transaction) => {
      const grants = await transaction.tripShareGrant.findMany({
        where: { tripId: oldTripId, revokedAt: null, expiresAt: { gt: now } },
        orderBy: [{ createdAt: 'asc' }, { id: 'asc' }],
      });
      let transferred = 0;

      for (const grant of grants) {
        const conflicting = await transaction.tripShareGrant.findFirst({
          where: {
            tripId: newTripId,
            createdByUserId: grant.createdByUserId,
            revokedAt: null,
          },
        });
        if (conflicting && conflicting.id !== grant.id) {
          await transaction.tripShareGrant.updateMany({
            where: { id: conflicting.id, revokedAt: null },
            data: {
              revokedAt: now,
              revokeReason: conflicting.expiresAt.getTime() <= now.getTime()
                ? 'EXPIRED'
                : 'CREATION_ROLLBACK',
            },
          });
        }

        const result = await transaction.tripShareGrant.updateMany({
          where: {
            id: grant.id,
            tripId: oldTripId,
            revokedAt: null,
            expiresAt: { gt: now },
          },
          data: { tripId: newTripId },
        });
        transferred += result.count;
      }

      return transferred;
    }, { isolationLevel: 'Serializable' });
  }
}
