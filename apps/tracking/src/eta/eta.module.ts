import { Module } from '@nestjs/common';
import { EtaService } from './eta.service';
import { TRIP_DATA_PROVIDER } from './eta.constants';
import { NoopTripDataProvider } from './noop-trip-data.provider';

@Module({
  providers: [
    EtaService,
    { provide: TRIP_DATA_PROVIDER, useClass: NoopTripDataProvider },
  ],
  exports: [EtaService, TRIP_DATA_PROVIDER],
})
export class EtaModule {}
