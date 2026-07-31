import type { GpsUpdateEvent } from '../location/location.service';
import type { TripStopSnapshot } from './trip-data.provider';

export interface EtaProviderResult {
  distanceMeters: number;
  etaMinutes: number;
}

export interface EtaProvider {
  calculate(gps: GpsUpdateEvent, stop: TripStopSnapshot): Promise<EtaProviderResult | null>;
}
