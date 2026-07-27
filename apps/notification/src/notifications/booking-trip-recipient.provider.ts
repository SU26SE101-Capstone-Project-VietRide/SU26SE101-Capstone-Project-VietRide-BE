import { Inject, Injectable, UnauthorizedException } from '@nestjs/common';
import { SignJWT } from 'jose';
import { z } from 'zod';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import {
  INTERNAL_AUTH_HEADER,
  INTERNAL_JWT_AUDIENCE,
  INTERNAL_JWT_ISSUER,
} from './fcm-push.constants';

const BOOKING_LOOKUP_TIMEOUT_MS = 5_000;
const bookingTripRecipientsSchema = z
  .object({
    tripId: z.string().uuid(),
    recipients: z.array(
      z
        .object({
          bookingId: z.string().uuid(),
          userId: z.string().uuid(),
          status: z.enum(['CONFIRMED', 'PARTIAL_NO_SHOW']),
        })
        .strict(),
    ),
  })
  .strict();

@Injectable()
export class BookingTripRecipientProvider {
  constructor(@Inject(ENV_TOKEN) private readonly env: Env) {}

  async resolveTripPassengerUserIds(tripId: string): Promise<string[]> {
    const projection = await this.getTripRecipients(tripId);
    return [...new Set(projection.recipients.map((recipient) => recipient.userId))];
  }

  async resolveAffectedTripPassengerUserIds(
    tripId: string,
    affectedBookingIds: readonly string[],
  ): Promise<string[]> {
    if (affectedBookingIds.length === 0) return [];
    const affected = new Set(affectedBookingIds);
    const projection = await this.getTripRecipients(tripId);
    return [
      ...new Set(
        projection.recipients
          .filter((recipient) => affected.has(recipient.bookingId))
          .map((recipient) => recipient.userId),
      ),
    ];
  }

  private async getTripRecipients(
    tripId: string,
  ): Promise<z.infer<typeof bookingTripRecipientsSchema>> {
    const response = await fetch(
      new URL(
        `/internal/v1/bookings/trips/${tripId}/notification-recipients`,
        this.env.BOOKING_INTERNAL_BASE_URL,
      ),
      {
        headers: { [INTERNAL_AUTH_HEADER]: `Bearer ${await this.signInternalJwt()}` },
        signal: AbortSignal.timeout(BOOKING_LOOKUP_TIMEOUT_MS),
      },
    );
    if (response.status === 401) {
      throw new UnauthorizedException({
        errorCode: 'BOOKING_INTERNAL_AUTH_FAILED',
        detail: 'Booking rejected notification internal auth token',
      });
    }
    if (!response.ok) throw new Error(`BOOKING_RECIPIENT_LOOKUP_FAILED_${response.status}`);

    const projection = bookingTripRecipientsSchema.parse(await response.json());
    if (projection.tripId !== tripId) throw new Error('BOOKING_RECIPIENT_TRIP_MISMATCH');
    return projection;
  }

  private async signInternalJwt(): Promise<string> {
    if (!this.env.INTERNAL_JWT_SECRET) {
      throw new Error('INTERNAL_JWT_SECRET_REQUIRED_FOR_BOOKING_RECIPIENT_LOOKUP');
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
