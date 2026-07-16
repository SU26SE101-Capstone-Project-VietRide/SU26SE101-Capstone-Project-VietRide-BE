import { RedisService } from '@vietride/nest-redis';
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
  let redisMultiSet: jest.Mock;
  let redisMultiDel: jest.Mock;
  let redisMultiExec: jest.Mock;
  let service: MessageIdempotencyService;

  beforeEach(() => {
    redisGet = jest.fn();
    redisSet = jest.fn();
    redisDel = jest.fn();
    redisMultiSet = jest.fn();
    redisMultiDel = jest.fn();
    redisMultiExec = jest.fn();
    const multi = {
      set: redisMultiSet.mockReturnThis(),
      del: redisMultiDel.mockReturnThis(),
      exec: redisMultiExec,
    };
    const redis = {
      getClient: jest.fn(() => ({
        get: redisGet,
        set: redisSet,
        del: redisDel,
        multi: jest.fn(() => multi),
      })),
    } as unknown as RedisService;
    service = new MessageIdempotencyService(redis);
  });

  it('returns duplicate when processed key already exists', async () => {
    redisGet.mockResolvedValue('1');

    await expect(service.begin(ROUTING_KEY, MESSAGE_ID)).resolves.toBe('duplicate');

    expect(redisSet).not.toHaveBeenCalled();
  });

  it('acquires processing lock for a new message', async () => {
    redisGet.mockResolvedValue(null);
    redisSet.mockResolvedValue('OK');

    await expect(service.begin(ROUTING_KEY, MESSAGE_ID)).resolves.toBe('acquired');

    expect(redisSet).toHaveBeenCalledWith(
      `notification:idem:processing:${ROUTING_KEY}:${MESSAGE_ID}`,
      '1',
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
    await service.markProcessed(ROUTING_KEY, MESSAGE_ID);

    expect(redisMultiSet).toHaveBeenCalledWith(
      `notification:idem:processed:${ROUTING_KEY}:${MESSAGE_ID}`,
      '1',
      'EX',
      RABBITMQ_IDEMPOTENCY_TTL_SECONDS,
    );
    expect(redisMultiDel).toHaveBeenCalledWith(
      `notification:idem:processing:${ROUTING_KEY}:${MESSAGE_ID}`,
    );
    expect(redisMultiExec).toHaveBeenCalled();
  });

  it('releases processing lock for transient failures', async () => {
    await service.release(ROUTING_KEY, MESSAGE_ID);

    expect(redisDel).toHaveBeenCalledWith(
      `notification:idem:processing:${ROUTING_KEY}:${MESSAGE_ID}`,
    );
  });
});
