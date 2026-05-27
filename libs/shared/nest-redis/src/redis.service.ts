import { Inject, Injectable, Logger, OnModuleDestroy } from '@nestjs/common';
import Redis, { type Redis as RedisClient } from 'ioredis';

export const REDIS_CLIENT = Symbol('REDIS_CLIENT');

@Injectable()
export class RedisService implements OnModuleDestroy {
  private readonly logger = new Logger('RedisService');

  constructor(@Inject(REDIS_CLIENT) private readonly client: RedisClient) {}

  getClient(): RedisClient {
    return this.client;
  }

  async get(key: string): Promise<string | null> {
    return this.client.get(key);
  }

  async set(key: string, value: string, ttlSec?: number): Promise<void> {
    if (ttlSec && ttlSec > 0) {
      await this.client.set(key, value, 'EX', ttlSec);
    } else {
      await this.client.set(key, value);
    }
  }

  async del(...keys: string[]): Promise<number> {
    if (keys.length === 0) return 0;
    return this.client.del(...keys);
  }

  async expire(key: string, ttlSec: number): Promise<number> {
    return this.client.expire(key, ttlSec);
  }

  async onModuleDestroy(): Promise<void> {
    try {
      await this.client.quit();
    } catch (err) {
      this.logger.warn(`Error closing Redis: ${(err as Error).message}`);
    }
  }
}

export type { RedisClient };
export { Redis as IORedis };
