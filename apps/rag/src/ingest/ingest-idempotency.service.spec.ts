import { RedisService } from '@vietride/nest-redis';
import { IngestIdempotencyService } from './ingest-idempotency.service';

const OPERATION_ID = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';

describe('IngestIdempotencyService', () => {
  it('acquires an owner-token lease and completes it atomically', async () => {
    const client = {
      get: jest.fn().mockResolvedValue(null),
      set: jest.fn().mockResolvedValue('OK'),
      eval: jest.fn().mockResolvedValue(1),
    };
    const service = new IngestIdempotencyService({
      getClient: jest.fn(() => client),
    } as unknown as RedisService);

    const lease = await service.begin(OPERATION_ID);

    expect(lease.state).toBe('acquired');
    if (lease.state !== 'acquired') throw new Error('lease was not acquired');
    expect(client.set).toHaveBeenCalledWith(
      expect.stringContaining(OPERATION_ID),
      lease.ownerToken,
      'EX',
      expect.any(Number),
      'NX',
    );
    await expect(service.markProcessed(OPERATION_ID, lease.ownerToken)).resolves.toBeUndefined();
    expect(client.eval).toHaveBeenCalledWith(
      expect.any(String),
      2,
      expect.stringContaining('processing'),
      expect.stringContaining('processed'),
      lease.ownerToken,
      expect.any(String),
    );
  });

  it('does not let a stale worker mark a newer owner lease processed', async () => {
    const client = {
      get: jest.fn().mockResolvedValue(null),
      set: jest.fn().mockResolvedValue('OK'),
      eval: jest.fn().mockResolvedValue(0),
    };
    const service = new IngestIdempotencyService({
      getClient: jest.fn(() => client),
    } as unknown as RedisService);

    await expect(service.markProcessed(OPERATION_ID, 'stale-owner')).rejects.toThrow(
      'RAG_INGEST_LOCK_NOT_OWNED',
    );
    await expect(service.release(OPERATION_ID, 'stale-owner')).resolves.toBeUndefined();
  });

  it('returns duplicate before attempting to acquire a processing lease', async () => {
    const client = {
      get: jest.fn().mockResolvedValue('1'),
      set: jest.fn(),
    };
    const service = new IngestIdempotencyService({
      getClient: jest.fn(() => client),
    } as unknown as RedisService);

    await expect(service.begin(OPERATION_ID)).resolves.toEqual({ state: 'duplicate' });
    expect(client.set).not.toHaveBeenCalled();
  });
});
