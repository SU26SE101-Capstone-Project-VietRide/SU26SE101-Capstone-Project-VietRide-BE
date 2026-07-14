import { Inject, Injectable, NotFoundException, UnauthorizedException } from '@nestjs/common';
import { SignJWT } from 'jose';
import { z } from 'zod';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import {
  INTERNAL_AUTH_HEADER,
  INTERNAL_JWT_AUDIENCE,
  INTERNAL_JWT_ISSUER,
} from './fcm-push.constants';

const TripSnapshotSchema = z.object({
  operatorId: z.string().uuid(),
  driverUserId: z.string().uuid().nullable(),
  assistantUserId: z.string().uuid().nullable(),
});

@Injectable()
export class TripAnnouncementRecipientProvider {
  constructor(@Inject(ENV_TOKEN) private readonly env: Env) {}

  async resolveTripCrewUserIds(tripId: string, operatorId: string): Promise<string[]> {
    const token = await this.signInternalJwt();
    const response = await fetch(
      new URL(`/internal/v1/trips/${tripId}`, this.env.TRIP_INTERNAL_BASE_URL),
      {
        headers: { [INTERNAL_AUTH_HEADER]: `Bearer ${token}` },
        signal: AbortSignal.timeout(5_000),
      },
    );
    if (response.status === 401) {
      throw new UnauthorizedException({
        errorCode: 'TRIP_INTERNAL_AUTH_FAILED',
        detail: 'Trip rejected internal auth',
      });
    }
    if (response.status === 404) {
      throw new NotFoundException({
        errorCode: 'TRIP_NOT_FOUND',
        detail: `Trip ${tripId} was not found`,
      });
    }
    if (!response.ok) throw new Error(`TRIP_SNAPSHOT_LOOKUP_FAILED_${response.status}`);

    const snapshot = TripSnapshotSchema.parse(await response.json());
    if (snapshot.operatorId !== operatorId) {
      throw new NotFoundException({
        errorCode: 'TRIP_NOT_FOUND',
        detail: `Trip ${tripId} was not found`,
      });
    }
    return [snapshot.driverUserId, snapshot.assistantUserId].filter((value): value is string =>
      Boolean(value),
    );
  }

  private async signInternalJwt(): Promise<string> {
    if (!this.env.INTERNAL_JWT_SECRET)
      throw new Error('INTERNAL_JWT_SECRET_REQUIRED_FOR_TRIP_LOOKUP');
    return new SignJWT({})
      .setProtectedHeader({ alg: 'HS256', typ: 'JWT' })
      .setSubject('notification-service')
      .setIssuer(INTERNAL_JWT_ISSUER)
      .setAudience(INTERNAL_JWT_AUDIENCE)
      .setIssuedAt()
      .setExpirationTime(`${this.env.INTERNAL_JWT_TTL_SEC}s`)
      .sign(new TextEncoder().encode(this.env.INTERNAL_JWT_SECRET));
  }
}
