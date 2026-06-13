import { Inject, Injectable, ServiceUnavailableException } from '@nestjs/common';
import { EMBEDDING_PROVIDER, ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import type { EmbeddingProvider } from '../providers/embedding.provider';

const PROBE_TEXT = 'VietRide embedding dimension probe';

@Injectable()
export class EmbeddingDimensionProbeService {
  constructor(
    @Inject(ENV_TOKEN) private readonly env: Env,
    @Inject(EMBEDDING_PROVIDER) private readonly embeddingProvider: EmbeddingProvider,
  ) {}

  async probe(): Promise<number> {
    const embedding = await this.embeddingProvider.embed({ input: PROBE_TEXT });
    const dimension = embedding.length;
    if (
      this.env.RAG_EMBEDDING_DIMENSIONS !== 'auto' &&
      dimension !== this.env.RAG_EMBEDDING_DIMENSIONS
    ) {
      throw new ServiceUnavailableException({
        errorCode: 'RAG_EMBEDDING_DIMENSION_MISMATCH',
        detail: 'Embedding provider dimension does not match configured dimension',
      });
    }
    return dimension;
  }
}
