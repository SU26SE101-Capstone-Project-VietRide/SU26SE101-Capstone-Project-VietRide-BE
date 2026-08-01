export interface TripStopSnapshot {
  stopId: string;
  latitude: number;
  longitude: number;
  sequence: number;
  status?: string;
  alertRecipientUserIds?: string[];
  estimatedArrivalTime?: string;
}

export interface TripDataProvider {
  getRouteStops(tripId: string): Promise<TripStopSnapshot[]>;
}
