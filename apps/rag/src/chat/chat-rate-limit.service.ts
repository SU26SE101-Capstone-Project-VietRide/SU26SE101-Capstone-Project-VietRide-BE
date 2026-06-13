import { HttpException, HttpStatus, Inject, Injectable } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import { ENV_TOKEN } from '../app/tokens';
import type { RagInternalUser } from '../auth/rag-internal-user.types';
import type { Env } from '../config/env.schema';

const RATE_LIMIT_WINDOW_SECONDS = 60 * 60;
const OPERATOR_SCOPED_ROLES = new Set([
  'DRIVER',
  'ASSISTANT',
  'OPERATOR_STAFF',
  'OPERATOR_ADMIN',
]);

@Injectable()
export class ChatRateLimitService {
  constructor(
    private readonly redis: RedisService,
    @Inject(ENV_TOKEN) private readonly env: Env,
  ) {}

  async assertAllowed(user: RagInternalUser): Promise<void> {
    const key = this.resolveKey(user);
    const limit = this.resolveLimit(user.role);
    const count = await this.incrementWindow(key);

    if (count > limit) {
      throw new HttpException(
        {
          errorCode: 'RAG_RATE_LIMIT_EXCEEDED',
          detail: 'RAG chat rate limit exceeded',
        },
        HttpStatus.TOO_MANY_REQUESTS,
      );
    }
  }

  private async incrementWindow(key: string): Promise<number> {
    const client = this.redis.getClient();
    const count = await client.incr(key);
    if (count === 1) {
      await client.expire(key, RATE_LIMIT_WINDOW_SECONDS);
    }
    return count;
  }

  private resolveKey(user: RagInternalUser): string {
    if (user.role && OPERATOR_SCOPED_ROLES.has(user.role)) {
      return `rag:chat:rate:operator:${user.operatorId ?? user.sub}`;
    }
    return `rag:chat:rate:user:${user.sub}`;
  }

  private resolveLimit(role: string | undefined): number {
    if (role && OPERATOR_SCOPED_ROLES.has(role)) {
      return this.env.RAG_OPERATOR_RATE_LIMIT_PER_HOUR;
    }
    return this.env.RAG_USER_RATE_LIMIT_PER_HOUR;
  }
}
