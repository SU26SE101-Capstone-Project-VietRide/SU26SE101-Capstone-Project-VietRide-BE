import { INestApplication } from '@nestjs/common';
import { APP_FILTER, APP_INTERCEPTOR } from '@nestjs/core';
import { Test } from '@nestjs/testing';
import { ApiResponseExceptionFilter, ApiResponseInterceptor } from '@vietride/nest-common';
import { RedisService } from '@vietride/nest-redis';
import { SignJWT } from 'jose';
import type { AddressInfo } from 'node:net';
import { ENV_TOKEN } from '../app/tokens';
import { InternalJwtAuthGuard } from '../auth/internal-jwt-auth.guard';
import type { Policy } from '../generated/rag-prisma-client';
import { RagPrismaService } from '../prisma/rag-prisma.service';
import { RagIdempotencyInterceptor } from '../swagger/rag-idempotency.interceptor';
import { RagIdempotencyService } from '../swagger/rag-idempotency.service';
import { AdminPoliciesController } from './admin-policies.controller';
import { IdentityPolicyActorProvider } from './identity-policy-actor.provider';
import { OperatorPoliciesController } from './operator-policies.controller';
import { PoliciesRepository } from './policies.repository';
import { PoliciesService } from './policies.service';

const SECRET = 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa';
const ADMIN_ID = '11111111-1111-4111-8111-111111111111';
const OPERATOR_ADMIN_ID = '22222222-2222-4222-8222-222222222222';
const OPERATOR_ID = '33333333-3333-4333-8333-333333333333';
const OTHER_OPERATOR_ID = '44444444-4444-4444-8444-444444444444';
const POLICY_ID = '55555555-5555-4555-8555-555555555555';

describe('Policies controllers (e2e)', () => {
  let app: INestApplication;
  let baseUrl: string;
  let repository: jest.Mocked<PoliciesRepository>;
  let actors: jest.Mocked<IdentityPolicyActorProvider>;
  const redisValues = new Map<string, string>();

  beforeAll(async () => {
    repository = {
      list: jest.fn(),
      findById: jest.fn(),
      createWithAudit: jest.fn(),
      updateWithAudit: jest.fn(),
      softDeleteWithAudit: jest.fn(),
    } as unknown as jest.Mocked<PoliciesRepository>;
    actors = {
      resolve: jest.fn(),
    } as unknown as jest.Mocked<IdentityPolicyActorProvider>;
    const redisClient = {
      get: jest.fn(async (key: string) => redisValues.get(key) ?? null),
      set: jest.fn(async (key: string, value: string, ...args: string[]) => {
        if (args.includes('NX') && redisValues.has(key)) return null;
        redisValues.set(key, value);
        return 'OK';
      }),
      eval: jest.fn(async (_script: string, keyCount: number, ...args: Array<string | number>) => {
        const keys = args.slice(0, keyCount).map(String);
        const scriptArgs = args.slice(keyCount).map(String);
        const processingKey = keys[0];
        if (!processingKey || redisValues.get(processingKey) !== scriptArgs[0]) return 0;
        if (keyCount === 2 && keys[1]) redisValues.set(keys[1], scriptArgs[1] ?? '');
        redisValues.delete(processingKey);
        return 1;
      }),
    };
    const moduleRef = await Test.createTestingModule({
      controllers: [AdminPoliciesController, OperatorPoliciesController],
      providers: [
        PoliciesService,
        InternalJwtAuthGuard,
        RagIdempotencyService,
        { provide: PoliciesRepository, useValue: repository },
        { provide: IdentityPolicyActorProvider, useValue: actors },
        { provide: RedisService, useValue: { getClient: () => redisClient } },
        {
          provide: RagPrismaService,
          useValue: {
            idempotencyOperation: {
              create: jest.fn().mockResolvedValue({}),
              findUnique: jest.fn().mockResolvedValue(null),
              deleteMany: jest.fn().mockResolvedValue({ count: 1 }),
              updateMany: jest.fn().mockResolvedValue({ count: 1 }),
            },
          },
        },
        { provide: ENV_TOKEN, useValue: { INTERNAL_JWT_SECRET: SECRET } },
        { provide: APP_FILTER, useValue: new ApiResponseExceptionFilter() },
        { provide: APP_INTERCEPTOR, useClass: RagIdempotencyInterceptor },
        { provide: APP_INTERCEPTOR, useValue: new ApiResponseInterceptor() },
      ],
    }).compile();
    app = moduleRef.createNestApplication({ rawBody: true });
    await app.listen(0);
    baseUrl = `http://127.0.0.1:${(app.getHttpServer().address() as AddressInfo).port}`;
  });

  afterAll(async () => app.close());

  beforeEach(() => {
    jest.clearAllMocks();
    redisValues.clear();
    actors.resolve.mockResolvedValue({
      displayName: 'Policy Admin',
      email: 'admin@vietride.vn',
    });
    repository.list.mockResolvedValue({ items: [makePolicy()], totalItems: 1 });
    repository.findById.mockResolvedValue(makePolicy());
    repository.createWithAudit.mockImplementation(async (input) =>
      makePolicy({
        operatorId: input.operatorId,
        title: input.title,
        createdByUserId: input.actor.userId,
      }),
    );
    repository.updateWithAudit.mockImplementation(async (input) => ({
      state: 'updated',
      policy: makePolicy({
        operatorId: input.operatorId,
        ...input.changes,
        version: input.nextVersion,
        rowVersion: 1,
      }),
    }));
    repository.softDeleteWithAudit.mockImplementation(async (input) => ({
      state: 'deleted',
      policy: makePolicy({
        operatorId: input.operatorId,
        deletedAt: new Date('2026-07-30T00:00:00.000Z'),
        rowVersion: 1,
      }),
    }));
  });

  it('returns an ADR-wrapped paged platform list for SYSTEM_ADMIN', async () => {
    const response = await fetch(`${baseUrl}/v1/admin/policies?page=1&pageSize=20`, {
      headers: { 'X-Internal-Auth': await token(ADMIN_ID, 'SYSTEM_ADMIN') },
    });
    const body = await response.json();

    expect(response.status).toBe(200);
    expect(body).toMatchObject({
      success: true,
      data: { items: [{ id: POLICY_ID, operatorId: null }], totalItems: 1 },
    });
    expect(repository.list).toHaveBeenCalledWith(
      null,
      expect.objectContaining({ page: 1, pageSize: 20 }),
    );
  });

  it('uses only the verified operatorId claim for operator list scope', async () => {
    repository.list.mockResolvedValue({
      items: [makePolicy({ operatorId: OPERATOR_ID })],
      totalItems: 1,
    });
    const response = await fetch(`${baseUrl}/v1/operator/policies`, {
      headers: {
        'X-Internal-Auth': await token(OPERATOR_ADMIN_ID, 'OPERATOR_ADMIN', OPERATOR_ID),
      },
    });

    expect(response.status).toBe(200);
    expect(repository.list).toHaveBeenCalledWith(OPERATOR_ID, expect.objectContaining({ page: 1 }));
  });

  it('rejects an unknown list filter instead of silently broadening the result', async () => {
    const response = await fetch(`${baseUrl}/v1/admin/policies?actve=false`, {
      headers: { 'X-Internal-Auth': await token(ADMIN_ID, 'SYSTEM_ADMIN') },
    });
    const body = await response.json();

    expect(response.status).toBe(422);
    expect(body).toMatchObject({ error: { code: 'VALIDATION_ERROR' } });
    expect(repository.list).not.toHaveBeenCalled();
  });

  it('gets a platform Policy and rejects a malformed Policy ID with the frozen 422 contract', async () => {
    const authorization = await token(ADMIN_ID, 'SYSTEM_ADMIN');
    const found = await fetch(`${baseUrl}/v1/admin/policies/${POLICY_ID}`, {
      headers: { 'X-Internal-Auth': authorization },
    });
    const malformed = await fetch(`${baseUrl}/v1/admin/policies/not-a-uuid`, {
      headers: { 'X-Internal-Auth': authorization },
    });

    expect(found.status).toBe(200);
    expect(await found.json()).toMatchObject({ data: { id: POLICY_ID, operatorId: null } });
    expect(repository.findById).toHaveBeenCalledWith(POLICY_ID, null);
    expect(malformed.status).toBe(422);
    expect(await malformed.json()).toMatchObject({ error: { code: 'VALIDATION_ERROR' } });
  });

  it('returns 403 for OPERATOR_STAFF and does not read Policy data', async () => {
    const response = await fetch(`${baseUrl}/v1/operator/policies`, {
      headers: {
        'X-Internal-Auth': await token(OPERATOR_ADMIN_ID, 'OPERATOR_STAFF', OPERATOR_ID),
      },
    });

    expect(response.status).toBe(403);
    expect(repository.list).not.toHaveBeenCalled();
  });

  it('masks a cross-tenant Policy as POLICY_NOT_FOUND', async () => {
    repository.findById.mockResolvedValue(null);
    const response = await fetch(`${baseUrl}/v1/operator/policies/${POLICY_ID}`, {
      headers: {
        'X-Internal-Auth': await token(OPERATOR_ADMIN_ID, 'OPERATOR_ADMIN', OTHER_OPERATOR_ID),
      },
    });
    const body = await response.json();

    expect(response.status).toBe(404);
    expect(body).toMatchObject({ error: { code: 'POLICY_NOT_FOUND' } });
    expect(repository.findById).toHaveBeenCalledWith(POLICY_ID, OTHER_OPERATOR_ID);
  });

  it('requires Idempotency-Key before creating a Policy', async () => {
    const response = await fetch(`${baseUrl}/v1/admin/policies`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Internal-Auth': await token(ADMIN_ID, 'SYSTEM_ADMIN'),
      },
      body: JSON.stringify(createBody()),
    });
    const body = await response.json();

    expect(response.status).toBe(422);
    expect(body).toMatchObject({ error: { code: 'IDEMPOTENCY_KEY_REQUIRED' } });
    expect(repository.createWithAudit).not.toHaveBeenCalled();
  });

  it('rejects unknown server-managed create fields through strict Zod validation', async () => {
    const response = await fetch(`${baseUrl}/v1/admin/policies`, {
      method: 'POST',
      headers: mutationHeaders(
        await token(ADMIN_ID, 'SYSTEM_ADMIN'),
        '66666666-6666-4666-8666-666666666666',
      ),
      body: JSON.stringify({ ...createBody(), operatorId: OPERATOR_ID }),
    });
    const body = await response.json();

    expect(response.status).toBe(422);
    expect(body).toMatchObject({ error: { code: 'VALIDATION_ERROR' } });
    expect(repository.createWithAudit).not.toHaveBeenCalled();
  });

  it('byte-replays create without a second Identity lookup or DB mutation', async () => {
    const operationId = '77777777-7777-4777-8777-777777777777';
    const headers = mutationHeaders(await token(ADMIN_ID, 'SYSTEM_ADMIN'), operationId);
    const payload = JSON.stringify(createBody());
    const first = await fetch(`${baseUrl}/v1/admin/policies`, {
      method: 'POST',
      headers,
      body: payload,
    });
    const firstBody = await first.text();
    const replay = await fetch(`${baseUrl}/v1/admin/policies`, {
      method: 'POST',
      headers,
      body: payload,
    });

    expect(first.status).toBe(201);
    expect(replay.status).toBe(201);
    expect(await replay.text()).toBe(firstBody);
    expect(actors.resolve).toHaveBeenCalledTimes(1);
    expect(repository.createWithAudit).toHaveBeenCalledTimes(1);
  });

  it('updates and byte-replays a platform Policy exactly once', async () => {
    const operationId = '88888888-8888-4888-8888-888888888888';
    const headers = mutationHeaders(await token(ADMIN_ID, 'SYSTEM_ADMIN'), operationId);
    const payload = JSON.stringify({ version: 1, title: 'Updated Refund Policy' });
    const first = await fetch(`${baseUrl}/v1/admin/policies/${POLICY_ID}`, {
      method: 'PATCH',
      headers,
      body: payload,
    });
    const firstBody = await first.text();
    const replay = await fetch(`${baseUrl}/v1/admin/policies/${POLICY_ID}`, {
      method: 'PATCH',
      headers,
      body: payload,
    });

    expect(first.status).toBe(200);
    expect(replay.status).toBe(200);
    expect(await replay.text()).toBe(firstBody);
    expect(JSON.parse(firstBody)).toMatchObject({
      data: { title: 'Updated Refund Policy', version: 2 },
    });
    expect(repository.updateWithAudit).toHaveBeenCalledTimes(1);
    expect(repository.updateWithAudit).toHaveBeenCalledWith(
      expect.objectContaining({ operatorId: null, action: 'UPDATE', nextVersion: 2 }),
    );
  });

  it('rejects a no-op platform PATCH with 422 VALIDATION_ERROR', async () => {
    const response = await fetch(`${baseUrl}/v1/admin/policies/${POLICY_ID}`, {
      method: 'PATCH',
      headers: mutationHeaders(
        await token(ADMIN_ID, 'SYSTEM_ADMIN'),
        '99999999-9999-4999-8999-999999999999',
      ),
      body: JSON.stringify({ version: 1, title: 'Refund Policy' }),
    });

    expect(response.status).toBe(422);
    expect(await response.json()).toMatchObject({ error: { code: 'VALIDATION_ERROR' } });
    expect(repository.updateWithAudit).not.toHaveBeenCalled();
  });

  it('soft-deletes and byte-replays a platform Policy exactly once', async () => {
    const headers = mutationHeaders(
      await token(ADMIN_ID, 'SYSTEM_ADMIN'),
      'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
    );
    const first = await fetch(`${baseUrl}/v1/admin/policies/${POLICY_ID}`, {
      method: 'DELETE',
      headers,
    });
    const firstBody = await first.text();
    const replay = await fetch(`${baseUrl}/v1/admin/policies/${POLICY_ID}`, {
      method: 'DELETE',
      headers,
    });

    expect(first.status).toBe(200);
    expect(replay.status).toBe(200);
    expect(await replay.text()).toBe(firstBody);
    expect(repository.softDeleteWithAudit).toHaveBeenCalledTimes(1);
    expect(repository.softDeleteWithAudit).toHaveBeenCalledWith(
      expect.objectContaining({ operatorId: null, action: 'DELETE' }),
    );
  });

  it('creates, updates, and deletes only in the operator tenant from JWT', async () => {
    const authorization = await token(OPERATOR_ADMIN_ID, 'OPERATOR_ADMIN', OPERATOR_ID);
    const create = await fetch(`${baseUrl}/v1/operator/policies`, {
      method: 'POST',
      headers: mutationHeaders(authorization, 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'),
      body: JSON.stringify(createBody()),
    });
    const update = await fetch(`${baseUrl}/v1/operator/policies/${POLICY_ID}`, {
      method: 'PATCH',
      headers: mutationHeaders(authorization, 'cccccccc-cccc-4ccc-8ccc-cccccccccccc'),
      body: JSON.stringify({ version: 1, active: false }),
    });
    const remove = await fetch(`${baseUrl}/v1/operator/policies/${POLICY_ID}`, {
      method: 'DELETE',
      headers: mutationHeaders(authorization, 'dddddddd-dddd-4ddd-8ddd-dddddddddddd'),
    });

    expect(create.status).toBe(201);
    expect(await create.json()).toMatchObject({ data: { operatorId: OPERATOR_ID } });
    expect(update.status).toBe(200);
    expect(await update.json()).toMatchObject({ data: { operatorId: OPERATOR_ID, version: 1 } });
    expect(remove.status).toBe(200);
    expect(repository.createWithAudit).toHaveBeenCalledWith(
      expect.objectContaining({ operatorId: OPERATOR_ID }),
    );
    expect(repository.updateWithAudit).toHaveBeenCalledWith(
      expect.objectContaining({
        operatorId: OPERATOR_ID,
        action: 'DEACTIVATE',
        nextVersion: 1,
      }),
    );
    expect(repository.softDeleteWithAudit).toHaveBeenCalledWith(
      expect.objectContaining({ operatorId: OPERATOR_ID, action: 'DELETE' }),
    );
  });

  it('returns 503 and performs no mutation when Identity actor lookup fails', async () => {
    actors.resolve.mockRejectedValueOnce(new Error('Identity unavailable'));
    const response = await fetch(`${baseUrl}/v1/admin/policies`, {
      method: 'POST',
      headers: mutationHeaders(
        await token(ADMIN_ID, 'SYSTEM_ADMIN'),
        'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee',
      ),
      body: JSON.stringify(createBody()),
    });

    expect(response.status).toBe(503);
    expect(await response.json()).toMatchObject({ error: { code: 'UPSTREAM_UNAVAILABLE' } });
    expect(repository.createWithAudit).not.toHaveBeenCalled();
  });
});

function createBody() {
  return {
    title: 'Refund Policy',
    description: 'Refund rules',
    content: 'Canonical content',
    policyType: 'FOR_USER',
    category: 'REFUND',
    active: true,
  };
}

function mutationHeaders(internalToken: string, operationId: string) {
  return {
    'Content-Type': 'application/json',
    'X-Internal-Auth': internalToken,
    'Idempotency-Key': operationId,
  };
}

async function token(sub: string, role: string, operatorId?: string): Promise<string> {
  const jwt = await new SignJWT({ sub, role, ...(operatorId ? { operatorId } : {}) })
    .setProtectedHeader({ alg: 'HS256', typ: 'JWT' })
    .setIssuer('vietride-gateway')
    .setAudience('vietride-internal')
    .setIssuedAt()
    .setExpirationTime('120s')
    .sign(new TextEncoder().encode(SECRET));
  return `Bearer ${jwt}`;
}

function makePolicy(overrides: Partial<Policy> = {}): Policy {
  return {
    id: POLICY_ID,
    operatorId: null,
    title: 'Refund Policy',
    description: 'Refund rules',
    content: 'Canonical content',
    policyType: 'FOR_USER',
    category: 'REFUND',
    version: 1,
    active: true,
    createdByUserId: ADMIN_ID,
    createdByDisplayName: 'Policy Admin',
    createdByEmail: 'admin@vietride.vn',
    rowVersion: 0,
    createdAt: new Date('2026-07-29T10:00:00.000Z'),
    updatedAt: new Date('2026-07-29T10:00:00.000Z'),
    deletedAt: null,
    ...overrides,
  };
}
