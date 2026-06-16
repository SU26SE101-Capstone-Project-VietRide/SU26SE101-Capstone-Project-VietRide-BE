import { INestApplication } from '@nestjs/common';
import { Test, TestingModule } from '@nestjs/testing';
import { SignJWT } from 'jose';
import type { AddressInfo } from 'node:net';
import { ENV_TOKEN } from '../app/tokens';
import { InternalJwtAuthGuard } from '../auth/internal-jwt-auth.guard';
import { RuntimeConfigService, type RuntimeConfigItem } from '../config/runtime-config.service';
import { RuntimeConfigAdminController } from './runtime-config-admin.controller';
import { RuntimeConfigAdminService } from './runtime-config-admin.service';

const INTERNAL_JWT_SECRET = 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa';
const INTERNAL_JWT_ISSUER = 'vietride-gateway';
const INTERNAL_JWT_AUDIENCE = 'vietride-internal';
const ADMIN_USER_ID = '11111111-1111-1111-1111-111111111111';
const PASSENGER_USER_ID = '22222222-2222-2222-2222-222222222222';

describe('RuntimeConfigAdminController (e2e)', () => {
  let app: INestApplication;
  let baseUrl: string;
  let runtimeConfig: jest.Mocked<RuntimeConfigService>;

  beforeAll(async () => {
    runtimeConfig = {
      list: jest.fn(),
      update: jest.fn(),
      getDetail: jest.fn(),
      rollback: jest.fn(),
      reload: jest.fn(),
    } as unknown as jest.Mocked<RuntimeConfigService>;

    const moduleFixture: TestingModule = await Test.createTestingModule({
      controllers: [RuntimeConfigAdminController],
      providers: [
        RuntimeConfigAdminService,
        InternalJwtAuthGuard,
        { provide: RuntimeConfigService, useValue: runtimeConfig },
        { provide: ENV_TOKEN, useValue: { INTERNAL_JWT_SECRET } },
      ],
    }).compile();

    app = moduleFixture.createNestApplication();
    app.setGlobalPrefix('api');
    await app.listen(0);
    const address = app.getHttpServer().address() as AddressInfo;
    baseUrl = `http://127.0.0.1:${address.port}`;
  });

  afterAll(async () => {
    await app.close();
  });

  beforeEach(() => {
    jest.clearAllMocks();
    runtimeConfig.list.mockResolvedValue([makeConfigItem()]);
    runtimeConfig.update.mockImplementation(async (input) =>
      makeConfigItem({
        key: input.key,
        value: input.value as RuntimeConfigItem['value'],
        updatedByUserId: input.updatedByUserId,
      }),
    );
    runtimeConfig.reload.mockResolvedValue(undefined);
  });

  it('GET /api/v1/admin/rag-config returns config list for SYSTEM_ADMIN', async () => {
    const response = await fetch(`${baseUrl}/api/v1/admin/rag-config`, {
      headers: { 'X-Internal-Auth': await signInternalJwt(ADMIN_USER_ID, 'SYSTEM_ADMIN') },
    });
    const body = (await response.json()) as RuntimeConfigItem[];

    expect(response.status).toBe(200);
    expect(body).toHaveLength(1);
    const firstConfig = body[0];
    expect(firstConfig).toBeDefined();
    expect(firstConfig?.key).toBe('intent.off_topic_refusal');
  });

  it('GET /api/v1/admin/rag-config returns 401 without internal JWT', async () => {
    const response = await fetch(`${baseUrl}/api/v1/admin/rag-config`);

    expect(response.status).toBe(401);
  });

  it('PATCH /api/v1/admin/rag-config/:key returns 403 for non-admin caller', async () => {
    const response = await fetch(`${baseUrl}/api/v1/admin/rag-config/intent.off_topic_refusal`, {
      method: 'PATCH',
      headers: {
        'Content-Type': 'application/json',
        'X-Internal-Auth': await signInternalJwt(PASSENGER_USER_ID, 'PASSENGER'),
      },
      body: JSON.stringify({ value: 'Updated refusal' }),
    });

    expect(response.status).toBe(403);
    expect(runtimeConfig.update).not.toHaveBeenCalled();
  });

  it('PATCH /api/v1/admin/rag-config/:key updates config value for SYSTEM_ADMIN', async () => {
    const response = await fetch(`${baseUrl}/api/v1/admin/rag-config/intent.off_topic_refusal`, {
      method: 'PATCH',
      headers: {
        'Content-Type': 'application/json',
        'X-Internal-Auth': await signInternalJwt(ADMIN_USER_ID, 'SYSTEM_ADMIN'),
      },
      body: JSON.stringify({ value: 'Updated refusal', reason: 'Copy update' }),
    });
    const body = (await response.json()) as RuntimeConfigItem;

    expect(response.status).toBe(200);
    expect(body.value).toBe('Updated refusal');
    expect(runtimeConfig.update).toHaveBeenCalledWith(
      expect.objectContaining({
        key: 'intent.off_topic_refusal',
        value: 'Updated refusal',
        updatedByUserId: ADMIN_USER_ID,
        reason: 'Copy update',
      }),
    );
  });

  it('PATCH /api/v1/admin/rag-config/:key updates prompt config for SYSTEM_ADMIN', async () => {
    const response = await fetch(`${baseUrl}/api/v1/admin/rag-config/chat.system_prompt`, {
      method: 'PATCH',
      headers: {
        'Content-Type': 'application/json',
        'X-Internal-Auth': await signInternalJwt(ADMIN_USER_ID, 'SYSTEM_ADMIN'),
      },
      body: JSON.stringify({ value: 'Updated system prompt with {conversation_summary} and {retrieved_context}.' }),
    });
    const body = (await response.json()) as RuntimeConfigItem;

    expect(response.status).toBe(200);
    expect(body.key).toBe('chat.system_prompt');
    expect(runtimeConfig.update).toHaveBeenCalledWith(
      expect.objectContaining({
        key: 'chat.system_prompt',
        value: 'Updated system prompt with {conversation_summary} and {retrieved_context}.',
        updatedByUserId: ADMIN_USER_ID,
      }),
    );
  });

  it('POST /api/v1/admin/rag-config/reload reloads config cache for SYSTEM_ADMIN', async () => {
    const response = await fetch(`${baseUrl}/api/v1/admin/rag-config/reload`, {
      method: 'POST',
      headers: { 'X-Internal-Auth': await signInternalJwt(ADMIN_USER_ID, 'SYSTEM_ADMIN') },
    });
    const body = (await response.json()) as { reloaded?: boolean };

    expect(response.status).toBe(201);
    expect(body.reloaded).toBe(true);
    expect(runtimeConfig.reload).toHaveBeenCalled();
  });

  it('POST /api/v1/admin/rag-config/reload returns 403 for non-admin caller', async () => {
    const response = await fetch(`${baseUrl}/api/v1/admin/rag-config/reload`, {
      method: 'POST',
      headers: { 'X-Internal-Auth': await signInternalJwt(PASSENGER_USER_ID, 'PASSENGER') },
    });

    expect(response.status).toBe(403);
    expect(runtimeConfig.reload).not.toHaveBeenCalled();
  });
});

async function signInternalJwt(sub: string, role: string): Promise<string> {
  const token = await new SignJWT({ sub, role, reqId: 'req-rag-config' })
    .setProtectedHeader({ alg: 'HS256', typ: 'JWT' })
    .setIssuer(INTERNAL_JWT_ISSUER)
    .setAudience(INTERNAL_JWT_AUDIENCE)
    .setIssuedAt()
    .setExpirationTime('120s')
    .sign(new TextEncoder().encode(INTERNAL_JWT_SECRET));

  return `Bearer ${token}`;
}

function makeConfigItem(overrides: Partial<RuntimeConfigItem> = {}): RuntimeConfigItem {
  return {
    key: 'intent.off_topic_refusal',
    value: 'Default refusal',
    valueType: 'string',
    editableGroup: 'admin',
    riskLevel: 'low',
    requiresRestart: false,
    description: 'User-facing refusal text for off-topic questions.',
    updatedByUserId: null,
    updatedAt: null,
    ...overrides,
  };
}
