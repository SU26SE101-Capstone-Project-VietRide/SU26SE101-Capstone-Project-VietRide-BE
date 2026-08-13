import { Inject, Injectable, ServiceUnavailableException } from '@nestjs/common';
import { EMBEDDING_PROVIDER } from '../app/tokens';
import type { EmbeddingProvider } from '../providers/embedding.provider';
import { RAG_EMBEDDING_DIMENSIONS } from './embedding.constants';

const PROBE_TEXT = 'VietRide embedding dimension probe';

@Injectable()
export class EmbeddingDimensionProbeService {
  constructor(
    @Inject(EMBEDDING_PROVIDER) private readonly embeddingProvider: EmbeddingProvider,
  ) {}

  async probe(): Promise<number> {
    const embedding = await this.embeddingProvider.embed({ input: PROBE_TEXT });
    const dimension = embedding.length;
    if (dimension !== RAG_EMBEDDING_DIMENSIONS) {
      throw new ServiceUnavailableException({
        errorCode: 'RAG_EMBEDDING_DIMENSION_MISMATCH',
        detail: 'Embedding provider dimension does not match configured dimension',
      });
    }
    return dimension;
  }
}
