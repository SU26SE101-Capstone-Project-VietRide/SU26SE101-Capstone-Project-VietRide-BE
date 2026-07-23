import { Injectable } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import { createHash, randomUUID } from 'node:crypto';
import { NotificationPrismaService } from '../prisma/notification-prisma.service';
import {
  RABBITMQ_IDEMPOTENCY_TTL_SECONDS,
  RABBITMQ_PROCESSING_LOCK_TTL_SECONDS,
} from './core-events.constants';

export type MessageProcessingState = 'acquired' | 'duplicate' | 'locked';

@Injectable()
export class MessageIdempotencyService {
  private static readonly RELEASE_LOCK_SCRIPT = `
    if redis.call('GET', KEYS[1]) == ARGV[1] then
      return redis.call('DEL', KEYS[1])
    end
    return 0
  `;

  private readonly ownedLocks = new Map<
    string,
    { ownerToken: string; payloadHash: string; routingKey: string; messageId: string }
  >();

  constructor(
    private readonly redis: RedisService,
    private readonly prisma: NotificationPrismaService,
  ) {}

  async begin(
    routingKey: string,
    messageId: string,
    payload: Buffer | string = messageId,
  ): Promise<MessageProcessingState> {
    const client = this.redis.getClient();
    const processedKey = this.processedKey(routingKey, messageId);
    const processingKey = this.processingKey(routingKey, messageId);
    const payloadHash = createHash('sha256').update(payload).digest('hex').toUpperCase();

    const durable = await this.prisma.processedMessage.findUnique({
      where: { consumerName_messageId: { consumerName: routingKey, messageId } },
    });
    if (durable) {
      if (durable.payloadHash !== payloadHash) {
        throw new Error(`MESSAGE_PAYLOAD_MISMATCH_${routingKey}_${messageId}`);
      }
      return 'duplicate';
    }

    const alreadyProcessed = await client.get(processedKey);
    if (alreadyProcessed) return 'duplicate';

    const ownerToken = randomUUID();

    const acquired = await client.set(
      processingKey,
      ownerToken,
      'EX',
      RABBITMQ_PROCESSING_LOCK_TTL_SECONDS,
      'NX',
    );

    if (acquired !== 'OK') return 'locked';

    this.ownedLocks.set(processingKey, { ownerToken, payloadHash, routingKey, messageId });
    return 'acquired';
  }

  async markProcessed(routingKey: string, messageId: string): Promise<void> {
    const processingKey = this.processingKey(routingKey, messageId);
    const owned = this.ownedLocks.get(processingKey);
    if (!owned) throw new Error(`MESSAGE_LOCK_NOT_OWNED_${routingKey}_${messageId}`);

    await this.prisma.processedMessage.create({
      data: {
        consumerName: routingKey,
        messageId,
        routingKey,
        payloadHash: owned.payloadHash,
      },
    });
    await this.redis.getClient().set(
      this.processedKey(routingKey, messageId),
      '1',
      'EX',
      RABBITMQ_IDEMPOTENCY_TTL_SECONDS,
    );
    await this.releaseOwned(processingKey, owned.ownerToken);
  }

  async release(routingKey: string, messageId: string): Promise<void> {
    const processingKey = this.processingKey(routingKey, messageId);
    const owned = this.ownedLocks.get(processingKey);
    if (!owned) return;
    await this.releaseOwned(processingKey, owned.ownerToken);
  }

  private async releaseOwned(processingKey: string, ownerToken: string): Promise<void> {
    await this.redis
      .getClient()
      .eval(MessageIdempotencyService.RELEASE_LOCK_SCRIPT, 1, processingKey, ownerToken);
    this.ownedLocks.delete(processingKey);
  }

  private processedKey(routingKey: string, messageId: string): string {
    return `notification:idem:processed:${routingKey}:${messageId}`;
  }

  private processingKey(routingKey: string, messageId: string): string {
    return `notification:idem:processing:${routingKey}:${messageId}`;
  }
}
