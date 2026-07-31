import { Injectable } from '@nestjs/common';
import type { RouteGeometryProvider, RouteGeometrySnapshot } from './route-geometry.provider';

@Injectable()
export class NoopRouteGeometryProvider implements RouteGeometryProvider {
  peekCachedRouteGeometry(tripId: string): RouteGeometrySnapshot | null {
    void tripId;
    return null;
  }

  async getRouteGeometry(tripId: string): Promise<RouteGeometrySnapshot | null> {
    void tripId;
    return null;
  }
}
