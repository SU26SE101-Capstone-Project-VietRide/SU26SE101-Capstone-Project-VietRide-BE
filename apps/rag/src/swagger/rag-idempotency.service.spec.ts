import { ConflictException, UnprocessableEntityException } from '@nestjs/common';
import { RagPrismaService } from '../prisma/rag-prisma.service';
import { RagIdempotencyService } from './rag-idempotency.service';

const OPERATION_ID = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const USER_ID = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';

describe('RagIdempotencyService', () => {
  let service: RagIdempotencyService;
  let prisma: {
    idempotencyOperation: {
      create: jest.Mock;
      findUnique: jest.Mock;
      update: jest.Mock;
      updateMany: jest.Mock;
      deleteMany: jest.Mock;
    };
  };

  beforeEach(() => {
    prisma = {
      idempotencyOperation: {
        create: jest.fn(),
        findUnique: jest.fn(),
        update: jest.fn(),
        updateMany: jest.fn(),
        deleteMany: jest.fn(),
      },
    };
    service = new RagIdempotencyService(prisma as unknown as RagPrismaService);
  });

  it('acquires a new UUID operation with an owner token', async () => {
    const result = await service.begin(makeInput());

    expect(result.state).toBe('acquired');
    if (result.state === 'acquired') {
      expect(result.operationId).toBe(OPERATION_ID);
      expect(result.ownerToken).toMatch(/^[0-9a-f-]{36}$/i);
    }
  });

  it('replays a completed response for the same fingerprint', async () => {
    prisma.idempotencyOperation.create.mockRejectedValueOnce(new Error('unique'));
    prisma.idempotencyOperation.findUnique.mockResolvedValueOnce(
      existingOperation({
        status: 'COMPLETED',
        responseStatus: 200,
        responseHeaders: { 'content-type': 'text/event-stream' },
        responseBody: 'event: done\n\n',
      }),
    );

    await expect(service.begin(makeInput())).resolves.toEqual({
      state: 'replay',
      response: {
        statusCode: 200,
        headers: { 'content-type': 'text/event-stream' },
        body: 'event: done\n\n',
      },
    });
  });

  it('rejects the same operation UUID with a different fingerprint', async () => {
    prisma.idempotencyOperation.create.mockRejectedValueOnce(new Error('unique'));
    prisma.idempotencyOperation.findUnique.mockResolvedValueOnce(
      existingOperation({ fingerprint: 'different' }),
    );

    await expect(service.begin(makeInput())).rejects.toBeInstanceOf(
      UnprocessableEntityException,
    );
  });

  it('returns pending while a live owner is processing', async () => {
    prisma.idempotencyOperation.create.mockRejectedValueOnce(new Error('unique'));
    prisma.idempotencyOperation.findUnique.mockResolvedValueOnce(existingOperation());

    await expect(service.begin(makeInput())).rejects.toBeInstanceOf(ConflictException);
  });

  it('only completes an operation owned by the caller', async () => {
    prisma.idempotencyOperation.updateMany.mockResolvedValueOnce({ count: 0 });

    await expect(
      service.complete(OPERATION_ID, 'cccccccc-cccc-4ccc-8ccc-cccccccccccc', {
        statusCode: 200,
        headers: {},
        body: '{}',
      }),
    ).rejects.toThrow('RAG_IDEMPOTENCY_LOCK_NOT_OWNED');
  });
});

function makeInput() {
  return {
    operationId: OPERATION_ID,
    userId: USER_ID,
    method: 'POST',
    path: '/v1/rag/chat',
    fingerprint: 'A'.repeat(64),
  };
}

function existingOperation(overrides: Record<string, unknown> = {}) {
  return {
    operationId: OPERATION_ID,
    userId: USER_ID,
    method: 'POST',
    path: '/v1/rag/chat',
    fingerprint: 'A'.repeat(64),
    ownerToken: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc',
    status: 'PROCESSING',
    responseStatus: null,
    responseHeaders: null,
    responseBody: null,
    createdAt: new Date(),
    updatedAt: new Date(),
    expiresAt: new Date(Date.now() + 60_000),
    ...overrides,
  };
}
