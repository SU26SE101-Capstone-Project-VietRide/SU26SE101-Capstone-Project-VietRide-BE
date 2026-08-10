import { Module } from '@nestjs/common';
import { EtaModule } from '../eta/eta.module';
import { TrackingPrismaModule } from '../prisma/prisma.module';
import { TripDelayQueueService } from './trip-delay-queue.service';
import { TripDelayService } from './trip-delay.service';
import { RouteStateGenerationModule } from '../route-state/route-state-generation.module';

@Module({
  imports: [EtaModule, TrackingPrismaModule, RouteStateGenerationModule],
  providers: [TripDelayService, TripDelayQueueService],
  exports: [TripDelayService],
})
export class TripDelayModule {}
