import { Module } from '@nestjs/common';
import { NoopRouteGeometryProvider } from './noop-route-geometry.provider';
import { OffRouteService } from './off-route.service';
import { ROUTE_GEOMETRY_PROVIDER } from './off-route.constants';

@Module({
  providers: [
    OffRouteService,
    { provide: ROUTE_GEOMETRY_PROVIDER, useClass: NoopRouteGeometryProvider },
  ],
  exports: [OffRouteService, ROUTE_GEOMETRY_PROVIDER],
})
export class OffRouteModule {}
