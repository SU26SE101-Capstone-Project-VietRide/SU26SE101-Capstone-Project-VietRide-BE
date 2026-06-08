import { Inject, Injectable } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import pino from 'pino';
import { TrackingPrismaService } from '../prisma/tracking-prisma.service';
import {
  APPROACHING_ALERT_DEDUPE_TTL_SECONDS,
  APPROACHING_ALERT_EVENT_TYPE,
  APPROACHING_ALERT_WAVE_1,
  APPROACHING_ALERT_WAVE_1_THRESHOLD_MINUTES,
  APPROACHING_ALERT_WAVE_2,
  APPROACHING_ALERT_WAVE_2_THRESHOLD_MINUTES,
  BOOKING_DATA_PROVIDER,
  trackingApproachingNotifiedKey,
  type ApproachingAlertWave,
} from './approaching-alert.constants';
import type { BookingDataProvider, PickupBookingSnapshot } from './booking-data.provider';

export interface ApproachingAlertEtaUpdate {
  tripId: string;
  stopId: string;
  etaMinutes: number;
}

export interface ApproachingAlertPayload {
  tripId: string;
  bookingId: string;
  passengerUserId?: string;
  stopId: string;
  etaMinutes: number;
  wave: ApproachingAlertWave;
}

@Injectable()
export class ApproachingAlertService {
  private readonly logger = pino({ name: ApproachingAlertService.name });

  constructor(
    private readonly redis: RedisService,
    private readonly prisma: TrackingPrismaService,
    @Inject(BOOKING_DATA_PROVIDER) private readonly bookingDataProvider: BookingDataProvider,
  ) {}

  async handleEtaUpdate(eta: ApproachingAlertEtaUpdate): Promise<number> {
    try {
      return await this.evaluateEtaUpdate(eta);
    } catch (error) {
      this.logger.warn({ err: error, tripId: eta.tripId, stopId: eta.stopId }, 'Skipping approaching alert evaluation');
      return 0;
    }
  }

  private async evaluateEtaUpdate(eta: ApproachingAlertEtaUpdate): Promise<number> {
    const waves = this.resolveEligibleWaves(eta.etaMinutes);
    if (waves.length === 0) return 0;

    const bookings = await this.bookingDataProvider.getPickupBookings(eta.tripId, eta.stopId);
    let created = 0;

    for (const booking of bookings) {
      if (!this.shouldProcessBooking(booking)) continue;

      for (const wave of waves) {
        const isFirstNotification = await this.markNotificationPending(eta.tripId, booking.bookingId, wave);
        if (!isFirstNotification) continue;

        await this.createOutboxEvent({
          tripId: eta.tripId,
          bookingId: booking.bookingId,
          ...(booking.passengerUserId ? { passengerUserId: booking.passengerUserId } : {}),
          stopId: eta.stopId,
          etaMinutes: eta.etaMinutes,
          wave,
        });
        created += 1;
      }
    }

    return created;
  }

  private resolveEligibleWaves(etaMinutes: number): ApproachingAlertWave[] {
    if (etaMinutes <= APPROACHING_ALERT_WAVE_2_THRESHOLD_MINUTES) {
      return [APPROACHING_ALERT_WAVE_1, APPROACHING_ALERT_WAVE_2];
    }

    if (etaMinutes <= APPROACHING_ALERT_WAVE_1_THRESHOLD_MINUTES) {
      return [APPROACHING_ALERT_WAVE_1];
    }

    return [];
  }

  private shouldProcessBooking(booking: PickupBookingSnapshot): boolean {
    if (booking.status === 'CANCELLED' || booking.status === 'NO_SHOW') return false;
    return booking.pickupStatus !== 'PICKED_UP' && booking.pickupStatus !== 'MISSED';
  }

  private async markNotificationPending(
    tripId: string,
    bookingId: string,
    wave: ApproachingAlertWave,
  ): Promise<boolean> {
    const result = await this.redis
      .getClient()
      .set(
        trackingApproachingNotifiedKey(tripId, bookingId, wave),
        '1',
        'EX',
        APPROACHING_ALERT_DEDUPE_TTL_SECONDS,
        'NX',
      );
    return result === 'OK';
  }

  private async createOutboxEvent(payload: ApproachingAlertPayload): Promise<void> {
    await this.prisma.outboxEvent.create({
      data: {
        eventType: APPROACHING_ALERT_EVENT_TYPE,
        payload: {
          tripId: payload.tripId,
          bookingIds: [payload.bookingId],
          ...(payload.passengerUserId ? { userId: payload.passengerUserId } : {}),
          stopId: payload.stopId,
          etaMinutes: payload.etaMinutes,
          wave: payload.wave,
        },
      },
    });
  }
}
