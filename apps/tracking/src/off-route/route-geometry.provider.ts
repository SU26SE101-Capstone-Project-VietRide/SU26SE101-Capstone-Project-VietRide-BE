export interface RouteGeometryPoint {
  latitude: number;
  longitude: number;
}

export interface RouteGeometrySnapshot {
  tripId: string;
  points: RouteGeometryPoint[];
}

export interface RouteGeometryProvider {
  getRouteGeometry(tripId: string): Promise<RouteGeometrySnapshot | null>;
}
