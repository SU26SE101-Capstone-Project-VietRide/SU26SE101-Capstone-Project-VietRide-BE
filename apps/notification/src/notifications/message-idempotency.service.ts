import { Injectable } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import {
  RABBITMQ_IDEMPOTENCY_TTL_SECONDS,
  RABBITMQ_PROCESSING_LOCK_TTL_SECONDS,
} from './core-events.constants';

export type MessageProcessingState = 'acquired' | 'duplicate' | 'locked';

@Injectable()
export class MessageIdempotencyService {
  constructor(private readonly redis: RedisService) {}

  async begin(routingKey: string, messageId: string): Promise<MessageProcessingState> {
    const client = this.redis.getClient();
    const processedKey = this.processedKey(routingKey, messageId);
    const processingKey = this.processingKey(routingKey, messageId);

    const alreadyProcessed = await client.get(processedKey);
    if (alreadyProcessed) return 'duplicate';

    const acquired = await client.set(
      processingKey,
      '1',
      'EX',
      RABBITMQ_PROCESSING_LOCK_TTL_SECONDS,
      'NX',
    );

    return acquired === 'OK' ? 'acquired' : 'locked';
  }

  async markProcessed(routingKey: string, messageId: string): Promise<void> {
    await this.redis
      .getClient()
      .multi()
      .set(this.processedKey(routingKey, messageId), '1', 'EX', RABBITMQ_IDEMPOTENCY_TTL_SECONDS)
      .del(this.processingKey(routingKey, messageId))
      .exec();
  }

  async release(routingKey: string, messageId: string): Promise<void> {
    await this.redis.getClient().del(this.processingKey(routingKey, messageId));
  }

  private processedKey(routingKey: string, messageId: string): string {
    return `notification:idem:processed:${routingKey}:${messageId}`;
  }

  private processingKey(routingKey: string, messageId: string): string {
    return `notification:idem:processing:${routingKey}:${messageId}`;
  }
}
