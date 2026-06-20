import { Module } from '@nestjs/common';
import { ApproachingAlertService } from './approaching-alert.service';
import { BOOKING_DATA_PROVIDER } from './approaching-alert.constants';
import { HttpBookingDataProvider } from './http-booking-data.provider';
import { TrackingInternalJwtSigner } from '../authorization/tracking-internal-jwt.signer';

@Module({
  providers: [
    ApproachingAlertService,
    TrackingInternalJwtSigner,
    { provide: BOOKING_DATA_PROVIDER, useClass: HttpBookingDataProvider },
  ],
  exports: [ApproachingAlertService, BOOKING_DATA_PROVIDER],
})
export class ApproachingAlertModule {}
