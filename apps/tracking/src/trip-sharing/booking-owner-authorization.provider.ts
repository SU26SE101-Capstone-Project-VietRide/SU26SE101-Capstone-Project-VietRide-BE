import {
  ForbiddenException,
  Inject,
  Injectable,
  ServiceUnavailableException,
} from '@nestjs/common';
import { randomUUID } from 'node:crypto';
import pino from 'pino';
import { z } from 'zod';
import { ENV_TOKEN } from '../app/tokens';
import { TrackingInternalJwtSigner } from '../authorization/tracking-internal-jwt.signer';
import type { Env } from '../config/env.schema';

const INTERNAL_AUTH_HEADER = 'X-Internal-Auth';
const AUTH_RESPONSE_SCHEMA = z.object({
  allowed: z.boolean(),
  scope: z.string().nullable().optional(),
}).passthrough();
const AUTH_ENVELOPE_SCHEMA = z.object({
  success: z.boolean(),
  data: AUTH_RESPONSE_SCHEMA.optional().nullable(),
  error: z.object({ code: z.string() }).passthrough().optional().nullable(),
}).passthrough();

@Injectable()
export class BookingOwnerAuthorizationProvider {
  private readonly logger = pino({ name: BookingOwnerAuthorizationProvider.name });

  constructor(
    @Inject(ENV_TOKEN) private readonly env: Env,
    private readonly signer: TrackingInternalJwtSigner,
  ) {}

  async requireBookingOwner(userId: string, tripId: string): Promise<void> {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), this.env.TRACKING_AUTH_HTTP_TIMEOUT_MS);
    try {
      const token = await this.signer.sign({
        sub: userId,
        role: 'PASSENGER',
        reqId: randomUUID(),
      });
      const response = await fetch(this.buildUrl(userId, tripId), {
        method: 'GET',
        headers: { [INTERNAL_AUTH_HEADER]: `Bearer ${token}` },
        signal: controller.signal,
      });

      if (response.status === 403 || response.status === 404) this.deny();
      if (response.status === 401 || !response.ok) this.unavailable();

      const body = await response.json().catch(() => null);
      const direct = AUTH_RESPONSE_SCHEMA.safeParse(body);
      const envelope = AUTH_ENVELOPE_SCHEMA.safeParse(body);
      if (
        envelope.success &&
        !envelope.data.success &&
        (envelope.data.error?.code === 'ACCESS_DENIED' || envelope.data.error?.code === 'TRIP_NOT_FOUND')
      ) {
        this.deny();
      }
      const result = direct.success
        ? direct.data
        : envelope.success && envelope.data.success && envelope.data.data
          ? envelope.data.data
          : null;
      if (!result) this.unavailable();
      if (!result.allowed || result.scope !== 'BOOKING_OWNER') this.deny();
    } catch (error) {
      if (error instanceof ForbiddenException || error instanceof ServiceUnavailableException) throw error;
      this.logger.warn({ tripId }, 'Booking ownership provider unavailable');
      this.unavailable();
    } finally {
      clearTimeout(timeout);
    }
  }

  private buildUrl(userId: string, tripId: string): string {
    const path = this.env.BOOKING_TRACKING_AUTH_PATH.replace(':tripId', encodeURIComponent(tripId));
    const url = new URL(path, this.env.BOOKING_SERVICE_BASE_URL);
    url.searchParams.set('userId', userId);
    url.searchParams.set('role', 'PASSENGER');
    return url.toString();
  }

  private deny(): never {
    throw new ForbiddenException({
      errorCode: 'ACCESS_DENIED',
      detail: 'User does not own a booking for this trip',
    });
  }

  private unavailable(): never {
    throw new ServiceUnavailableException({
      errorCode: 'TRACKING_AUTH_UNAVAILABLE',
      detail: 'Booking authorization provider is unavailable',
    });
  }
}
