import { Inject, Injectable } from '@nestjs/common';
import { z } from 'zod';
import { randomUUID } from 'node:crypto';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import {
  TrackingInternalJwtClaims,
  TrackingInternalJwtSigner,
} from '../authorization/tracking-internal-jwt.signer';
import type { BookingDataProvider, PickupBookingSnapshot } from './booking-data.provider';

const PickupBookingSchema = z.object({
  bookingId: z.string(),
  passengerUserId: z.string().optional(),
  stopId: z.string(),
  status: z.enum(['CONFIRMED', 'CHECKED_IN', 'CANCELLED', 'NO_SHOW']),
  pickupStatus: z.enum(['PENDING', 'PICKED_UP', 'MISSED']).optional(),
});

const PickupBookingsDataSchema = z.object({
  bookings: z.array(PickupBookingSchema),
});

const ApiResponseEnvelopeSchema = z.object({
  success: z.literal(true),
  data: z.unknown(),
});

const INTERNAL_AUTH_HEADER = 'X-Internal-Auth';
const BEARER_PREFIX = 'Bearer ';

@Injectable()
export class HttpBookingDataProvider implements BookingDataProvider {
  constructor(
    @Inject(ENV_TOKEN) private readonly env: Env,
    private readonly signer: TrackingInternalJwtSigner,
  ) {}

  async getPickupBookings(tripId: string, stopId: string): Promise<PickupBookingSnapshot[]> {
    const url = this.buildUrl(tripId, stopId);
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), this.env.TRACKING_DATA_PROVIDER_TIMEOUT_MS);

    try {
      const claims: TrackingInternalJwtClaims = {
        sub: 'tracking-service',
        reqId: randomUUID(),
      };
      const token = await this.signer.sign(claims);
      const response = await fetch(url, {
        method: 'GET',
        headers: {
          [INTERNAL_AUTH_HEADER]: `${BEARER_PREFIX}${token}`,
        },
        signal: controller.signal,
      });

      if (!response.ok) {
        return [];
      }

      const body = await response.json();

      const envelope = ApiResponseEnvelopeSchema.safeParse(body);
      if (!envelope.success) return [];

      const parsed = PickupBookingsDataSchema.safeParse(envelope.data.data);
      if (!parsed.success) return [];

      return parsed.data.bookings.map((booking) => ({
        bookingId: booking.bookingId,
        ...(booking.passengerUserId ? { passengerUserId: booking.passengerUserId } : {}),
        stopId: booking.stopId,
        status: booking.status,
        ...(booking.pickupStatus ? { pickupStatus: booking.pickupStatus } : {}),
      }));
    } catch {
      return [];
    } finally {
      clearTimeout(timeout);
    }
  }

  private buildUrl(tripId: string, stopId: string): string {
    let path = this.env.BOOKING_PICKUP_BOOKINGS_PATH
      .replace(':tripId', encodeURIComponent(tripId));
    path = path.replace(':stopId', encodeURIComponent(stopId));
    return new URL(path, this.env.BOOKING_SERVICE_BASE_URL).toString();
  }
}
