export interface RouteGeometryPoint {
  latitude: number;
  longitude: number;
}

export interface RouteGeometrySnapshot {
  tripId: string;
  points: RouteGeometryPoint[];
  alertRecipientUserIds?: string[];
}

export interface RouteGeometryProvider {
  getRouteGeometry(tripId: string): Promise<RouteGeometrySnapshot | null>;
}
