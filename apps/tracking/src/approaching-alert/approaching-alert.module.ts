import { Module } from '@nestjs/common';
import { ApproachingAlertService } from './approaching-alert.service';
import { BOOKING_DATA_PROVIDER } from './approaching-alert.constants';
import { NoopBookingDataProvider } from './noop-booking-data.provider';

@Module({
  providers: [
    ApproachingAlertService,
    { provide: BOOKING_DATA_PROVIDER, useClass: NoopBookingDataProvider },
  ],
  exports: [ApproachingAlertService, BOOKING_DATA_PROVIDER],
})
export class ApproachingAlertModule {}
