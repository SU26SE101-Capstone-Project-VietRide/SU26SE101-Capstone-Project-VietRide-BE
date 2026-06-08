import { Module } from '@nestjs/common';
import { EtaModule } from '../eta/eta.module';
import { TrackingPrismaModule } from '../prisma/prisma.module';
import { TripDelayQueueService } from './trip-delay-queue.service';
import { TripDelayService } from './trip-delay.service';

@Module({
  imports: [EtaModule, TrackingPrismaModule],
  providers: [TripDelayService, TripDelayQueueService],
  exports: [TripDelayService],
})
export class TripDelayModule {}
