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

const PARCEL_LOOKUP_TIMEOUT_MS = 5_000;
const ParcelSnapshotSchema = z
  .object({
    parcelId: z.string().uuid(),
    tripId: z.string().uuid(),
    status: z.string().trim().min(1),
    senderUserId: z.string().uuid(),
    recipientUserId: z.string().uuid().nullable(),
    operatorId: z.string().uuid(),
    dropoffStopId: z.string().uuid().nullable(),
  })
  .strict();
const ParcelSnapshotEnvelopeSchema = z
  .object({
    success: z.literal(true),
    statusCode: z.literal(200),
    data: ParcelSnapshotSchema,
    meta: z.object({}).passthrough(),
  })
  .passthrough();

export type ParcelRecipientSnapshot = z.infer<typeof ParcelSnapshotSchema>;

@Injectable()
export class ParcelRecipientProvider {
  constructor(@Inject(ENV_TOKEN) private readonly env: Env) {}

  async getParcelSnapshot(parcelId: string): Promise<ParcelRecipientSnapshot> {
    const response = await fetch(
      new URL(`/internal/v1/parcels/${parcelId}`, this.env.PARCEL_INTERNAL_BASE_URL),
      {
        headers: { [INTERNAL_AUTH_HEADER]: `Bearer ${await this.signInternalJwt()}` },
        signal: AbortSignal.timeout(PARCEL_LOOKUP_TIMEOUT_MS),
      },
    );
    if (response.status === 401) {
      throw new UnauthorizedException({
        errorCode: 'PARCEL_INTERNAL_AUTH_FAILED',
        detail: 'Parcel rejected notification internal auth token',
      });
    }
    if (response.status === 404) {
      throw new NotFoundException({
        errorCode: 'PARCEL_NOT_FOUND',
        detail: `Parcel ${parcelId} was not found`,
      });
    }
    if (!response.ok) throw new Error(`PARCEL_RECIPIENT_LOOKUP_FAILED_${response.status}`);

    const snapshot = ParcelSnapshotEnvelopeSchema.parse(await response.json()).data;
    if (snapshot.parcelId !== parcelId) throw new Error('PARCEL_RECIPIENT_ID_MISMATCH');
    return snapshot;
  }

  private async signInternalJwt(): Promise<string> {
    if (!this.env.INTERNAL_JWT_SECRET) {
      throw new Error('INTERNAL_JWT_SECRET_REQUIRED_FOR_PARCEL_RECIPIENT_LOOKUP');
    }
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
