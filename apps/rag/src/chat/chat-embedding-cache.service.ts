import { Injectable } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import { createHash } from 'node:crypto';

const EMBEDDING_CACHE_TTL_SECONDS = 60 * 60;

@Injectable()
export class ChatEmbeddingCacheService {
  constructor(private readonly redis: RedisService) {}

  async get(input: string): Promise<number[] | undefined> {
    const cached = await this.redis.get(this.key(input));
    if (!cached) return undefined;

    try {
      const parsed = JSON.parse(cached) as unknown;
      if (!Array.isArray(parsed) || parsed.some((value) => typeof value !== 'number')) {
        return undefined;
      }
      return parsed;
    } catch {
      return undefined;
    }
  }

  async set(input: string, embedding: number[]): Promise<void> {
    await this.redis.set(this.key(input), JSON.stringify(embedding), EMBEDDING_CACHE_TTL_SECONDS);
  }

  private key(input: string): string {
    const hash = createHash('sha256').update(input).digest('hex');
    return `rag:chat:embedding:${hash}`;
  }
}
