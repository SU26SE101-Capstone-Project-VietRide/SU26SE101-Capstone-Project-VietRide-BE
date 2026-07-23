import { Injectable } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import { randomUUID } from 'node:crypto';
import {
  RAG_INGEST_LOCK_TTL_SECONDS,
  RAG_INGEST_PROCESSED_TTL_SECONDS,
} from './ingest.constants';

export type RagIngestProcessingState =
  | { state: 'acquired'; ownerToken: string }
  | { state: 'duplicate' }
  | { state: 'locked' };

const MARK_PROCESSED_SCRIPT = `
if redis.call('GET', KEYS[1]) ~= ARGV[1] then return 0 end
redis.call('SET', KEYS[2], '1', 'EX', tonumber(ARGV[2]))
redis.call('DEL', KEYS[1])
return 1
`;

const RELEASE_SCRIPT = `
if redis.call('GET', KEYS[1]) == ARGV[1] then
  return redis.call('DEL', KEYS[1])
end
return 0
`;

@Injectable()
export class IngestIdempotencyService {
  constructor(private readonly redis: RedisService) {}

  async begin(operationId: string): Promise<RagIngestProcessingState> {
    const client = this.redis.getClient();
    const processed = await client.get(this.processedKey(operationId));
    if (processed) return { state: 'duplicate' };

    const ownerToken = randomUUID();
    const acquired = await client.set(
      this.processingKey(operationId),
      ownerToken,
      'EX',
      RAG_INGEST_LOCK_TTL_SECONDS,
      'NX',
    );

    return acquired === 'OK' ? { state: 'acquired', ownerToken } : { state: 'locked' };
  }

  async markProcessed(operationId: string, ownerToken: string): Promise<void> {
    const result = await this.redis.getClient().eval(
      MARK_PROCESSED_SCRIPT,
      2,
      this.processingKey(operationId),
      this.processedKey(operationId),
      ownerToken,
      String(RAG_INGEST_PROCESSED_TTL_SECONDS),
    );
    if (Number(result) !== 1) throw new Error('RAG_INGEST_LOCK_NOT_OWNED');
  }

  async release(operationId: string, ownerToken: string): Promise<void> {
    await this.redis
      .getClient()
      .eval(RELEASE_SCRIPT, 1, this.processingKey(operationId), ownerToken);
  }

  private processedKey(documentId: string): string {
    return `rag:ingest:processed:${documentId}`;
  }

  private processingKey(documentId: string): string {
    return `rag:ingest:processing:${documentId}`;
  }
}
