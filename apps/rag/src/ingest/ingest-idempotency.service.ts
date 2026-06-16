import { Injectable } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import {
  RAG_INGEST_LOCK_TTL_SECONDS,
  RAG_INGEST_PROCESSED_TTL_SECONDS,
} from './ingest.constants';

export type RagIngestProcessingState = 'acquired' | 'duplicate' | 'locked';

@Injectable()
export class IngestIdempotencyService {
  constructor(private readonly redis: RedisService) {}

  async begin(documentId: string): Promise<RagIngestProcessingState> {
    const client = this.redis.getClient();
    const processed = await client.get(this.processedKey(documentId));
    if (processed) return 'duplicate';

    const acquired = await client.set(
      this.processingKey(documentId),
      '1',
      'EX',
      RAG_INGEST_LOCK_TTL_SECONDS,
      'NX',
    );

    return acquired === 'OK' ? 'acquired' : 'locked';
  }

  async markProcessed(documentId: string): Promise<void> {
    await this.redis
      .getClient()
      .multi()
      .set(this.processedKey(documentId), '1', 'EX', RAG_INGEST_PROCESSED_TTL_SECONDS)
      .del(this.processingKey(documentId))
      .exec();
  }

  async release(documentId: string): Promise<void> {
    await this.redis.getClient().del(this.processingKey(documentId));
  }

  private processedKey(documentId: string): string {
    return `rag:ingest:processed:${documentId}`;
  }

  private processingKey(documentId: string): string {
    return `rag:ingest:processing:${documentId}`;
  }
}
