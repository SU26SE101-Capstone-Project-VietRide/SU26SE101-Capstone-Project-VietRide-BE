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
}
