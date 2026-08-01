import type { RedisService } from '@vietride/nest-redis';
import type { RagPrismaService } from '../prisma/rag-prisma.service';
import { RagIdempotencyService } from './rag-idempotency.service';

const OPERATION_ID = '11111111-1111-4111-8111-111111111111';
const FINGERPRINT = 'A'.repeat(64);

describe('RagIdempotencyService v2', () => {
  let client: {
    get: jest.Mock;
    set: jest.Mock;
    eval: jest.Mock;
  };
  let service: RagIdempotencyService;
  let legacyOperations: {
    create: jest.Mock;
    findUnique: jest.Mock;
    deleteMany: jest.Mock;
    updateMany: jest.Mock;
  };

  beforeEach(() => {
    client = {
      get: jest.fn().mockResolvedValue(null),
      set: jest.fn().mockResolvedValue('OK'),
      eval: jest.fn().mockResolvedValue(1),
    };
    legacyOperations = {
      create: jest.fn().mockResolvedValue({}),
      findUnique: jest.fn().mockResolvedValue(null),
      deleteMany: jest.fn().mockResolvedValue({ count: 1 }),
      updateMany: jest.fn().mockResolvedValue({ count: 1 }),
    };
    service = new RagIdempotencyService(
      { getClient: () => client } as unknown as RedisService,
      { idempotencyOperation: legacyOperations } as unknown as RagPrismaService,
    );
  });

  it('writes the old-node PostgreSQL barrier before taking the SHA-keyed Redis lock', async () => {
    const result = await service.begin({
      operationId: OPERATION_ID,
      userId: OPERATION_ID,
      method: 'POST',
      path: '/v1/admin/policies',
      fingerprint: FINGERPRINT,
    });

    expect(result.state).toBe('acquired');
    expect(client.set).toHaveBeenCalledWith(
      expect.stringMatching(/^rag:idem:v2:processing:[A-F0-9]{64}$/),
      expect.stringMatching(new RegExp(`^${FINGERPRINT}:[0-9a-f-]{36}$`, 'i')),
      'EX',
      120,
      'NX',
    );
    expect(client.set.mock.calls[0]?.[0]).not.toContain(OPERATION_ID);
    expect(legacyOperations.create).toHaveBeenCalledWith({
      data: expect.objectContaining({
        operationId: OPERATION_ID,
        fingerprint: FINGERPRINT,
        status: 'V2_BARRIER',
        expiresAt: expect.any(Date),
      }),
    });
    expect(legacyOperations.create.mock.invocationCallOrder[0]).toBeLessThan(
      client.set.mock.invocationCallOrder[0] ?? Number.MAX_SAFE_INTEGER,
    );
  });

  it('replays a completed response without acquiring another lock', async () => {
    client.get.mockResolvedValueOnce(
      JSON.stringify({
        fingerprint: FINGERPRINT,
        statusCode: 201,
        headers: { 'content-type': 'application/json; charset=utf-8' },
        body: '{"success":true,"statusCode":201}',
      }),
    );

    const result = await service.begin({
      operationId: OPERATION_ID,
      userId: OPERATION_ID,
      method: 'POST',
      path: '/v1/admin/policies',
      fingerprint: FINGERPRINT,
    });

    expect(result).toMatchObject({ state: 'replay', response: { statusCode: 201 } });
    expect(client.set).not.toHaveBeenCalled();
  });

  it('fails closed while a frozen legacy Redis idempotency key still exists', async () => {
    client.get.mockImplementation(async (key: string) =>
      key === `rag:idem:${OPERATION_ID}` ? 'legacy-body-hash' : null,
    );

    await expect(
      service.begin({
        operationId: OPERATION_ID,
        userId: OPERATION_ID,
        method: 'POST',
        path: '/v1/admin/policies',
        fingerprint: FINGERPRINT,
      }),
    ).rejects.toMatchObject({
      response: { errorCode: 'IDEMPOTENCY_KEY_MISMATCH' },
    });
    expect(client.set).not.toHaveBeenCalled();
  });

  it('fails closed while an unexpired pre-v2 PostgreSQL operation still exists', async () => {
    legacyOperations.create.mockRejectedValueOnce(new Error('duplicate operation id'));
    legacyOperations.findUnique.mockResolvedValueOnce({
      operationId: OPERATION_ID,
      userId: OPERATION_ID,
      method: 'POST',
      path: '/v1/admin/policies',
      fingerprint: FINGERPRINT,
      ownerToken: '22222222-2222-4222-8222-222222222222',
      status: 'COMPLETED',
      expiresAt: new Date(Date.now() + 60_000),
    });

    await expect(
      service.begin({
        operationId: OPERATION_ID,
        userId: OPERATION_ID,
        method: 'POST',
        path: '/v1/admin/policies',
        fingerprint: FINGERPRINT,
      }),
    ).rejects.toMatchObject({
      response: { errorCode: 'IDEMPOTENCY_KEY_MISMATCH' },
    });
    expect(client.set).not.toHaveBeenCalled();
  });

  it('uses one owner-safe Lua operation to store a 24-hour response and release the lock', async () => {
    const begin = await service.begin({
      operationId: OPERATION_ID,
      userId: OPERATION_ID,
      method: 'POST',
      path: '/v1/admin/policies',
      fingerprint: FINGERPRINT,
    });
    if (begin.state !== 'acquired') throw new Error('Expected acquired state');

    await service.complete(begin.operationId, begin.ownerToken, {
      statusCode: 201,
      headers: { 'content-type': 'application/json; charset=utf-8' },
      body: '{"success":true}',
    });

    expect(client.eval).toHaveBeenCalledWith(
      expect.stringContaining("redis.call('SET'"),
      2,
      expect.stringMatching(/^rag:idem:v2:processing:/),
      expect.stringMatching(/^rag:idem:v2:response:/),
      `${FINGERPRINT}:${begin.ownerToken}`,
      expect.stringContaining('"fingerprint"'),
      86_400,
    );
    expect(legacyOperations.updateMany).toHaveBeenCalledWith({
      where: {
        operationId: OPERATION_ID,
        ownerToken: begin.ownerToken,
        status: 'V2_BARRIER',
      },
      data: { expiresAt: expect.any(Date) },
    });
  });

  it('releases a failed owner only when its token still owns the processing lock', async () => {
    const begin = await service.begin({
      operationId: OPERATION_ID,
      userId: OPERATION_ID,
      method: 'POST',
      path: '/v1/admin/policies',
      fingerprint: FINGERPRINT,
    });
    if (begin.state !== 'acquired') throw new Error('Expected acquired state');

    await service.abandon(begin.operationId, begin.ownerToken);

    expect(client.eval).toHaveBeenCalledWith(
      expect.stringContaining("redis.call('DEL'"),
      1,
      expect.stringMatching(/^rag:idem:v2:processing:/),
      `${FINGERPRINT}:${begin.ownerToken}`,
    );
    expect(legacyOperations.deleteMany).toHaveBeenCalledWith({
      where: {
        operationId: OPERATION_ID,
        ownerToken: begin.ownerToken,
        status: 'V2_BARRIER',
      },
    });
  });

  it('retains the old-node barrier when response persistence starts but Redis completion fails', async () => {
    const begin = await service.begin({
      operationId: OPERATION_ID,
      userId: OPERATION_ID,
      method: 'POST',
      path: '/v1/admin/policies',
      fingerprint: FINGERPRINT,
    });
    if (begin.state !== 'acquired') throw new Error('Expected acquired state');
    client.eval.mockResolvedValueOnce(0);

    await expect(
      service.complete(begin.operationId, begin.ownerToken, {
        statusCode: 201,
        headers: { 'content-type': 'application/json' },
        body: '{"success":true}',
      }),
    ).rejects.toThrow('RAG_IDEMPOTENCY_LOCK_NOT_OWNED');
    legacyOperations.deleteMany.mockClear();

    await service.abandon(begin.operationId, begin.ownerToken);

    expect(legacyOperations.deleteMany).not.toHaveBeenCalled();
  });
});
