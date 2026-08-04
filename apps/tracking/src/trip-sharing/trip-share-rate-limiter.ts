import {
  HttpException,
  HttpStatus,
  Inject,
  Injectable,
  ServiceUnavailableException,
} from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import { createHash } from 'node:crypto';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';

const WINDOW_SECONDS = 60;
const MILLISECONDS_PER_SECOND = 1_000;
const RATE_LIMIT_SCRIPT = `
local current = redis.call('INCR', KEYS[1])
if current == 1 then
  redis.call('EXPIRE', KEYS[1], ARGV[1])
end
return current
`;

export type TripShareRateLimitSurface = 'context' | 'socket';

@Injectable()
export class TripShareRateLimiter {
  constructor(
    private readonly redis: RedisService,
    @Inject(ENV_TOKEN) private readonly env: Env,
  ) {}

  async consume(
    surface: TripShareRateLimitSurface,
    rawToken: string,
    nowMs: number = Date.now(),
  ): Promise<void> {
    const tokenHash = createHash('sha256').update(rawToken, 'utf8').digest('hex');
    const minuteBucket = Math.floor(nowMs / (WINDOW_SECONDS * MILLISECONDS_PER_SECOND));
    const key = `tracking:share:rate:${surface}:${tokenHash}:${minuteBucket}`;

    let count: number;
    try {
      const result = await this.redis.getClient().eval(RATE_LIMIT_SCRIPT, 1, key, WINDOW_SECONDS);
      count = Number(result);
      if (!Number.isFinite(count)) throw new Error('Invalid Redis rate-limit result');
    } catch {
      throw new ServiceUnavailableException({
        errorCode: 'TRACKING_SHARE_RATE_LIMIT_UNAVAILABLE',
        detail: 'Trip sharing rate limiting is unavailable',
      });
    }

    if (count > this.limitFor(surface)) {
      throw new HttpException(
        { errorCode: 'RATE_LIMITED', detail: 'Trip sharing rate limit exceeded' },
        HttpStatus.TOO_MANY_REQUESTS,
      );
    }
  }

  private limitFor(surface: TripShareRateLimitSurface): number {
    switch (surface) {
      case 'context':
        return this.env.TRACKING_SHARE_CONTEXT_RATE_LIMIT_PER_MIN;
      case 'socket':
        return this.env.TRACKING_SHARE_SOCKET_RATE_LIMIT_PER_MIN;
    }
  }
}
