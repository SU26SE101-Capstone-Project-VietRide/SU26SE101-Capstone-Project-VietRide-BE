import { randomUUID } from 'node:crypto';
import IORedis from 'ioredis';
import { RedisService } from '@vietride/nest-redis';
import { NotificationPrismaService } from '../prisma/notification-prisma.service';
import { MessageIdempotencyService } from './message-idempotency.service';

const describeSystem =
  process.env.NOTIFICATION_IDEMPOTENCY_SYSTEM_E2E === '1' ? describe : describe.skip;

describeSystem('MessageIdempotencyService (real PostgreSQL + Redis)', () => {
  const routingKey = 'notification.e2e.owner_lock';
  const messageIds: string[] = [];
  let redisClient: IORedis;
  let redis: RedisService;
  let prisma: NotificationPrismaService;

  beforeAll(async () => {
    redisClient = new IORedis(process.env.REDIS_URL as string, { maxRetriesPerRequest: null });
    redis = new RedisService(redisClient);
    prisma = new NotificationPrismaService();
    await prisma.$connect();
  });

  afterAll(async () => {
    await prisma.processedMessage.deleteMany({ where: { messageId: { in: messageIds } } });
    for (const messageId of messageIds) {
      await redisClient.del(
        `notification:idem:processed:${routingKey}:${messageId}`,
        `notification:idem:processing:${routingKey}:${messageId}`,
      );
    }
    await prisma.$disconnect();
    await redisClient.quit();
  });

  it('does not release a processing lock after another owner replaces its token', async () => {
    const messageId = randomUUID();
    messageIds.push(messageId);
    const service = new MessageIdempotencyService(redis, prisma);
    const processingKey = `notification:idem:processing:${routingKey}:${messageId}`;

    await expect(service.begin(routingKey, messageId, Buffer.from('owner-payload'))).resolves.toBe(
      'acquired',
    );
    await redisClient.set(processingKey, 'foreign-owner', 'EX', 60);

    await service.release(routingKey, messageId);

    await expect(redisClient.get(processingKey)).resolves.toBe('foreign-owner');
  });

  it('accepts an exact durable replay and rejects the same message identity with new bytes', async () => {
    const messageId = randomUUID();
    messageIds.push(messageId);
    const payload = Buffer.from('{"value":"original"}');
    const service = new MessageIdempotencyService(redis, prisma);

    await expect(service.begin(routingKey, messageId, payload)).resolves.toBe('acquired');
    await service.markProcessed(routingKey, messageId);

    const replayService = new MessageIdempotencyService(redis, prisma);
    await expect(replayService.begin(routingKey, messageId, payload)).resolves.toBe('duplicate');
    await expect(
      replayService.begin(routingKey, messageId, Buffer.from('{"value":"changed"}')),
    ).rejects.toThrow(`MESSAGE_PAYLOAD_MISMATCH_${routingKey}_${messageId}`);
  });
});
