import { Module } from '@nestjs/common';
import { ProvidersModule } from '../providers/providers.module';
import { EmbeddingDimensionProbeService } from './embedding-dimension-probe.service';

@Module({
  imports: [ProvidersModule],
  providers: [EmbeddingDimensionProbeService],
  exports: [EmbeddingDimensionProbeService],
})
export class EmbeddingModule {}
