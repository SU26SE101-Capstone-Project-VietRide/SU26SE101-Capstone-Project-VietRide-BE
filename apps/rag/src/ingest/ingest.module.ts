import { Module } from '@nestjs/common';
import { ProvidersModule } from '../providers/providers.module';
import { IngestIdempotencyService } from './ingest-idempotency.service';
import { IngestRepository } from './ingest.repository';
import { IngestService } from './ingest.service';
import { IngestWorkerService } from './ingest-worker.service';

@Module({
  imports: [ProvidersModule],
  providers: [IngestIdempotencyService, IngestRepository, IngestService, IngestWorkerService],
  exports: [IngestService],
})
export class IngestModule {}
