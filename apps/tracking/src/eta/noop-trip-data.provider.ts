import { Injectable } from '@nestjs/common';
import type { TripDataProvider, TripStopSnapshot } from './trip-data.provider';

@Injectable()
export class NoopTripDataProvider implements TripDataProvider {
  async getRouteStops(tripId: string): Promise<TripStopSnapshot[]> {
    void tripId;
    return [];
  }

  invalidateRouteStops(tripId: string): void {
    void tripId;
  }
}
