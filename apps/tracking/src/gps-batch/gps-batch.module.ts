import { Module } from '@nestjs/common';
import { GpsBatchFlushService } from './gps-batch-flush.service';

@Module({
  providers: [GpsBatchFlushService],
  exports: [GpsBatchFlushService],
})
export class GpsBatchModule {}
