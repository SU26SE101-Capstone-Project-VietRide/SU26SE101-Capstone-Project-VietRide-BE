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

export interface TripSharePublicCoordinateDto {
  latitude: number;
  longitude: number;
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
  status: 'IN_PROGRESS' | 'VEHICLE_REPLACEMENT_PENDING';
  expiresAt: string;
  lastUpdatedAt: string | null;
  vehicle: { location: TripSharePublicLocationDto | null };
  route: {
    originName: string;
    destinationName: string;
    origin: TripSharePublicCoordinateDto | null;
    destination: TripSharePublicCoordinateDto | null;
    stops: TripSharePublicStopDto[];
    geometry: TripSharePublicGeometryDto | null;
  };
  eta: TripSharePublicEtaDto | null;
}
