import { Module } from '@nestjs/common';
import { EtaService } from './eta.service';
import { GOOGLE_ETA_PROVIDER, LOCAL_ETA_PROVIDER, TRIP_DATA_PROVIDER } from './eta.constants';
import { HttpTripDataProvider } from './http-trip-data.provider';
import { TrackingInternalJwtSigner } from '../authorization/tracking-internal-jwt.signer';
import { OffRouteModule } from '../off-route/off-route.module';
import { GoogleRoutesEtaProvider } from './google-routes-eta.provider';
import { LocalRouteEtaProvider } from './local-route-eta.provider';
import { RouteStateGenerationModule } from '../route-state/route-state-generation.module';

@Module({
  imports: [OffRouteModule, RouteStateGenerationModule],
  providers: [
    EtaService,
    TrackingInternalJwtSigner,
    GoogleRoutesEtaProvider,
    { provide: TRIP_DATA_PROVIDER, useClass: HttpTripDataProvider },
    { provide: GOOGLE_ETA_PROVIDER, useExisting: GoogleRoutesEtaProvider },
    { provide: LOCAL_ETA_PROVIDER, useClass: LocalRouteEtaProvider },
  ],
  exports: [EtaService, TRIP_DATA_PROVIDER, GoogleRoutesEtaProvider],
})
export class EtaModule {}
