import { Module } from '@nestjs/common';
import { OffRouteService } from './off-route.service';
import { ROUTE_GEOMETRY_PROVIDER } from './off-route.constants';
import { HttpRouteGeometryProvider } from './http-route-geometry.provider';
import { TrackingInternalJwtSigner } from '../authorization/tracking-internal-jwt.signer';
import { RouteStateGenerationModule } from '../route-state/route-state-generation.module';

@Module({
  imports: [RouteStateGenerationModule],
  providers: [
    OffRouteService,
    TrackingInternalJwtSigner,
    { provide: ROUTE_GEOMETRY_PROVIDER, useClass: HttpRouteGeometryProvider },
  ],
  exports: [OffRouteService, ROUTE_GEOMETRY_PROVIDER],
})
export class OffRouteModule {}
