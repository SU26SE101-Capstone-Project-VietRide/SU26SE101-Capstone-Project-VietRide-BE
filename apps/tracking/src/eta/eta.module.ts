import { Module } from '@nestjs/common';
import { EtaService } from './eta.service';
import { GOONG_ETA_PROVIDER, LOCAL_ETA_PROVIDER, TRIP_DATA_PROVIDER } from './eta.constants';
import { HttpTripDataProvider } from './http-trip-data.provider';
import { TrackingInternalJwtSigner } from '../authorization/tracking-internal-jwt.signer';
import { OffRouteModule } from '../off-route/off-route.module';
import { GoongDirectionsEtaProvider } from './goong-directions-eta.provider';
import { LocalRouteEtaProvider } from './local-route-eta.provider';
import { RouteStateGenerationModule } from '../route-state/route-state-generation.module';

@Module({
  imports: [OffRouteModule, RouteStateGenerationModule],
  providers: [
    EtaService,
    TrackingInternalJwtSigner,
    GoongDirectionsEtaProvider,
    { provide: TRIP_DATA_PROVIDER, useClass: HttpTripDataProvider },
    { provide: GOONG_ETA_PROVIDER, useExisting: GoongDirectionsEtaProvider },
    { provide: LOCAL_ETA_PROVIDER, useClass: LocalRouteEtaProvider },
  ],
  exports: [EtaService, TRIP_DATA_PROVIDER, GoongDirectionsEtaProvider],
})
export class EtaModule {}
