import { Injectable } from '@nestjs/common';
import type { BookingDataProvider, PickupBookingSnapshot } from './booking-data.provider';

@Injectable()
export class NoopBookingDataProvider implements BookingDataProvider {
  async getPickupBookings(tripId: string, stopId: string): Promise<PickupBookingSnapshot[]> {
    void tripId;
    void stopId;
    return [];
  }
}
