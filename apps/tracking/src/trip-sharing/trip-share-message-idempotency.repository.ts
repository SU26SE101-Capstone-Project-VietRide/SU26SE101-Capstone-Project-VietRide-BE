import { Injectable } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import { randomUUID } from 'node:crypto';
import {
  TRIP_SHARE_EVENT_PROCESSED_TTL_SECONDS,
  TRIP_SHARE_EVENT_PROCESSING_TTL_SECONDS,
} from './trip-terminal-share.constants';

const MARK_PROCESSED_SCRIPT = `
local current = redis.call('GET', KEYS[1])
if current ~= ARGV[1] then return 0 end
redis.call('SET', KEYS[2], '1', 'EX', ARGV[2])
redis.call('DEL', KEYS[1])
return 1
`;

const RELEASE_SCRIPT = `
local current = redis.call('GET', KEYS[1])
if current ~= ARGV[1] then return 0 end
redis.call('DEL', KEYS[1])
return 1
`;

@Injectable()
export class TripShareMessageIdempotencyRepository {
  constructor(private readonly redis: RedisService) {}

  async isProcessed(messageIdentity: string): Promise<boolean> {
    return (await this.redis.getClient().get(this.processedKey(messageIdentity))) !== null;
  }

  async acquire(messageIdentity: string): Promise<string | null> {
    const ownerToken = randomUUID();
    const acquired = await this.redis.getClient().set(
      this.processingKey(messageIdentity),
      ownerToken,
      'EX',
      TRIP_SHARE_EVENT_PROCESSING_TTL_SECONDS,
      'NX',
    );
    return acquired === 'OK' ? ownerToken : null;
  }

  async markProcessed(messageIdentity: string, ownerToken: string): Promise<boolean> {
    const result = await this.redis.getClient().eval(
      MARK_PROCESSED_SCRIPT,
      2,
      this.processingKey(messageIdentity),
      this.processedKey(messageIdentity),
      ownerToken,
      TRIP_SHARE_EVENT_PROCESSED_TTL_SECONDS,
    );
    return Number(result) === 1;
  }

  async release(messageIdentity: string, ownerToken: string): Promise<boolean> {
    const result = await this.redis.getClient().eval(
      RELEASE_SCRIPT,
      1,
      this.processingKey(messageIdentity),
      ownerToken,
    );
    return Number(result) === 1;
  }

  private processedKey(messageIdentity: string): string {
    return `tracking:trip-share:event:processed:${messageIdentity}`;
  }

  private processingKey(messageIdentity: string): string {
    return `tracking:trip-share:event:processing:${messageIdentity}`;
  }
}
