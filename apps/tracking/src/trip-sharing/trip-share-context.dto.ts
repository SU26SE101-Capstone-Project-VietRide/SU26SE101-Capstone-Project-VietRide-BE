export interface TripSharePublicLocationDto {
  latitude: number;
  longitude: number;
  heading: number | null;
  speedKph: number | null;
  recordedAt: string;
}

export interface TripSharePublicGeometryDto {
  type: 'LineString';
  coordinates: [number, number][];
}

export interface TripSharePublicStopDto {
  name: string;
  latitude: number;
  longitude: number;
  sequence: number;
}

export interface TripSharePublicEtaDto {
  estimatedArrivalAt: string;
  remainingSeconds: number;
  delayMinutes: number | null;
  updatedAt: string;
}

export interface TripShareContextDto {
  status: 'IN_PROGRESS';
  expiresAt: string;
  lastUpdatedAt: string | null;
  vehicle: { location: TripSharePublicLocationDto | null };
  route: {
    originName: string;
    destinationName: string;
    stops: TripSharePublicStopDto[];
    geometry: TripSharePublicGeometryDto | null;
  };
  eta: TripSharePublicEtaDto | null;
}
