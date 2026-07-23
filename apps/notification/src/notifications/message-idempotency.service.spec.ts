import { RedisService } from '@vietride/nest-redis';
import { NotificationPrismaService } from '../prisma/notification-prisma.service';
import {
  RABBITMQ_IDEMPOTENCY_TTL_SECONDS,
  RABBITMQ_PROCESSING_LOCK_TTL_SECONDS,
} from './core-events.constants';
import { MessageIdempotencyService } from './message-idempotency.service';

const ROUTING_KEY = 'booking.booking.confirmed';
const MESSAGE_ID = 'message-1';

describe('MessageIdempotencyService', () => {
  let redisGet: jest.Mock;
  let redisSet: jest.Mock;
  let redisDel: jest.Mock;
  let redisEval: jest.Mock;
  let prismaFindUnique: jest.Mock;
  let prismaCreate: jest.Mock;
  let service: MessageIdempotencyService;

  beforeEach(() => {
    redisGet = jest.fn();
    redisSet = jest.fn();
    redisDel = jest.fn();
    redisEval = jest.fn();
    prismaFindUnique = jest.fn().mockResolvedValue(null);
    prismaCreate = jest.fn();
    const redis = {
      getClient: jest.fn(() => ({
        get: redisGet,
        set: redisSet,
        del: redisDel,
        eval: redisEval,
      })),
    } as unknown as RedisService;
    const prisma = {
      processedMessage: {
        findUnique: prismaFindUnique,
        create: prismaCreate,
      },
    } as unknown as NotificationPrismaService;
    service = new MessageIdempotencyService(redis, prisma);
  });

  it('returns duplicate when processed key already exists', async () => {
    redisGet.mockResolvedValue('1');

    await expect(service.begin(ROUTING_KEY, MESSAGE_ID)).resolves.toBe('duplicate');

    expect(redisSet).not.toHaveBeenCalled();
  });

  it('returns durable duplicate and rejects a mismatched payload', async () => {
    prismaFindUnique.mockResolvedValue({ payloadHash: 'DIFFERENT' });

    await expect(service.begin(ROUTING_KEY, MESSAGE_ID, Buffer.from('payload'))).rejects.toThrow(
      'MESSAGE_PAYLOAD_MISMATCH',
    );
    expect(redisGet).not.toHaveBeenCalled();
  });

  it('acquires processing lock for a new message', async () => {
    redisGet.mockResolvedValue(null);
    redisSet.mockResolvedValue('OK');

    await expect(service.begin(ROUTING_KEY, MESSAGE_ID)).resolves.toBe('acquired');

    expect(redisSet).toHaveBeenCalledWith(
      `notification:idem:processing:${ROUTING_KEY}:${MESSAGE_ID}`,
      expect.stringMatching(/^[0-9a-f-]{36}$/),
      'EX',
      RABBITMQ_PROCESSING_LOCK_TTL_SECONDS,
      'NX',
    );
  });

  it('returns locked when another worker is processing the message', async () => {
    redisGet.mockResolvedValue(null);
    redisSet.mockResolvedValue(null);

    await expect(service.begin(ROUTING_KEY, MESSAGE_ID)).resolves.toBe('locked');
  });

  it('marks processed and clears processing lock after success', async () => {
    redisGet.mockResolvedValue(null);
    redisSet.mockResolvedValueOnce('OK').mockResolvedValueOnce('OK');
    await service.begin(ROUTING_KEY, MESSAGE_ID, Buffer.from('payload'));
    await service.markProcessed(ROUTING_KEY, MESSAGE_ID);

    expect(prismaCreate).toHaveBeenCalledWith({
      data: expect.objectContaining({
        consumerName: ROUTING_KEY,
        messageId: MESSAGE_ID,
        routingKey: ROUTING_KEY,
        payloadHash: expect.stringMatching(/^[0-9A-F]{64}$/),
      }),
    });
    expect(redisSet).toHaveBeenLastCalledWith(
      `notification:idem:processed:${ROUTING_KEY}:${MESSAGE_ID}`,
      '1',
      'EX',
      RABBITMQ_IDEMPOTENCY_TTL_SECONDS,
    );
    expect(redisEval).toHaveBeenCalledWith(
      expect.stringContaining("redis.call('GET'"),
      1,
      `notification:idem:processing:${ROUTING_KEY}:${MESSAGE_ID}`,
      expect.stringMatching(/^[0-9a-f-]{36}$/),
    );
  });

  it('releases processing lock for transient failures', async () => {
    redisGet.mockResolvedValue(null);
    redisSet.mockResolvedValue('OK');
    await service.begin(ROUTING_KEY, MESSAGE_ID, Buffer.from('payload'));
    await service.release(ROUTING_KEY, MESSAGE_ID);

    expect(redisEval).toHaveBeenCalledWith(
      expect.stringContaining("redis.call('GET'"),
      1,
      `notification:idem:processing:${ROUTING_KEY}:${MESSAGE_ID}`,
      expect.stringMatching(/^[0-9a-f-]{36}$/),
    );
  });
});
