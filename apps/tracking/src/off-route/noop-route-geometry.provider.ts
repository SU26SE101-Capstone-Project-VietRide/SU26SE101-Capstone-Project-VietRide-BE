import { Injectable } from '@nestjs/common';
import type { RouteGeometryProvider, RouteGeometrySnapshot } from './route-geometry.provider';

@Injectable()
export class NoopRouteGeometryProvider implements RouteGeometryProvider {
  async getRouteGeometry(tripId: string): Promise<RouteGeometrySnapshot | null> {
    void tripId;
    return null;
  }
}
