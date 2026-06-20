import { Module } from '@nestjs/common';
import { EtaService } from './eta.service';
import { TRIP_DATA_PROVIDER } from './eta.constants';
import { HttpTripDataProvider } from './http-trip-data.provider';
import { TrackingInternalJwtSigner } from '../authorization/tracking-internal-jwt.signer';

@Module({
  providers: [
    EtaService,
    TrackingInternalJwtSigner,
    { provide: TRIP_DATA_PROVIDER, useClass: HttpTripDataProvider },
  ],
  exports: [EtaService, TRIP_DATA_PROVIDER],
})
export class EtaModule {}
