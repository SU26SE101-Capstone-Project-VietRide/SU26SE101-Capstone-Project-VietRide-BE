import { Injectable } from '@nestjs/common';
import type {
  DetailedRouteGeometryProvider,
  RouteGeometryFetchResult,
  RouteGeometrySnapshot,
} from './route-geometry.provider';

@Injectable()
export class NoopRouteGeometryProvider implements DetailedRouteGeometryProvider {
  peekCachedRouteGeometry(tripId: string): RouteGeometrySnapshot | null {
    void tripId;
    return null;
  }

  async getRouteGeometry(tripId: string): Promise<RouteGeometrySnapshot | null> {
    void tripId;
    return null;
  }

  async getDetailedRouteGeometry(tripId: string): Promise<RouteGeometryFetchResult> {
    void tripId;
    return { kind: 'unavailable' };
  }
}
