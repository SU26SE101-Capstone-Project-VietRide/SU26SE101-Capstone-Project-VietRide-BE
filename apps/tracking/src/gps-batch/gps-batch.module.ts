import { Module } from '@nestjs/common';
import { GpsBatchQueueService } from './gps-batch-queue.service';
import { GpsBatchFlushService } from './gps-batch-flush.service';

@Module({
  providers: [GpsBatchFlushService, GpsBatchQueueService],
  exports: [GpsBatchFlushService],
})
export class GpsBatchModule {}
