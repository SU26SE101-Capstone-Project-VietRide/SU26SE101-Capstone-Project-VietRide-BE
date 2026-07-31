import {
  BadRequestException,
  Body,
  Controller,
  HttpCode,
  INestApplication,
  Post,
  UseGuards,
} from '@nestjs/common';
import { APP_FILTER, APP_INTERCEPTOR } from '@nestjs/core';
import { Test } from '@nestjs/testing';
import { ApiResponseExceptionFilter, ApiResponseInterceptor } from '@vietride/nest-common';
import { RedisService } from '@vietride/nest-redis';
import { SignJWT } from 'jose';
import type { AddressInfo } from 'node:net';
import { ENV_TOKEN } from '../app/tokens';
import { InternalJwtAuthGuard } from '../auth/internal-jwt-auth.guard';
import { RagPrismaService } from '../prisma/rag-prisma.service';
import { ApiIdempotencyRequired } from './idempotency.swagger';
import { RagIdempotencyInterceptor } from './rag-idempotency.interceptor';
import { RagIdempotencyService } from './rag-idempotency.service';

const SECRET = 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa';
const USER_ID = '11111111-1111-4111-8111-111111111111';
const OPERATION_ID = '22222222-2222-4222-8222-222222222222';

@Controller('v1/idempotency-v2-test')
@UseGuards(InternalJwtAuthGuard)
class IdempotencyV2TestController {
  calls = 0;

  @Post()
  @HttpCode(200)
  @ApiIdempotencyRequired()
  create(@Body() body: { value?: string; fail?: boolean }) {
    this.calls += 1;
    if (body.fail) {
      throw new BadRequestException({ errorCode: 'TEST_REJECTED', detail: 'Rejected once' });
    }
    return { value: body.value };
  }
}

describe('RAG idempotency v2 HTTP behavior (e2e)', () => {
  let app: INestApplication;
  let baseUrl: string;
  let controller: IdempotencyV2TestController;
  const values = new Map<string, string>();

  beforeAll(async () => {
    const client = {
      get: jest.fn(async (key: string) => values.get(key) ?? null),
      set: jest.fn(async (key: string, value: string, ...args: string[]) => {
        if (args.includes('NX') && values.has(key)) return null;
        values.set(key, value);
        return 'OK';
      }),
      eval: jest.fn(async (_script: string, keyCount: number, ...args: Array<string | number>) => {
        const keys = args.slice(0, keyCount).map(String);
        const scriptArgs = args.slice(keyCount).map(String);
        const processingKey = keys[0];
        if (!processingKey || values.get(processingKey) !== scriptArgs[0]) return 0;
        if (keyCount === 2) {
          const responseKey = keys[1];
          if (!responseKey) return 0;
          values.set(responseKey, scriptArgs[1] ?? '');
        }
        values.delete(processingKey);
        return 1;
      }),
    };
    const moduleRef = await Test.createTestingModule({
      controllers: [IdempotencyV2TestController],
      providers: [
        InternalJwtAuthGuard,
        RagIdempotencyService,
        { provide: RedisService, useValue: { getClient: () => client } },
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
    controller = app.get(IdempotencyV2TestController);
  });

  afterAll(async () => app.close());

  beforeEach(() => {
    values.clear();
    controller.calls = 0;
  });

  it('replays the byte-identical final ADR envelope and executes the handler once', async () => {
    const first = await postJson(OPERATION_ID, '{"value":"same"}');
    const replay = await postJson(OPERATION_ID, '{"value":"same"}');

    expect(first.status).toBe(200);
    expect(replay.status).toBe(200);
    expect(replay.body).toBe(first.body);
    expect(controller.calls).toBe(1);
  });

  it('treats different raw JSON bytes as a fingerprint mismatch even when parsed values match', async () => {
    const first = await postJson(OPERATION_ID, '{"value":"same"}');
    const mismatch = await postJson(OPERATION_ID, '{ "value": "same" }');

    expect(first.status).toBe(200);
    expect(mismatch.status).toBe(422);
    expect(JSON.parse(mismatch.body)).toMatchObject({
      error: { code: 'IDEMPOTENCY_KEY_MISMATCH' },
    });
    expect(controller.calls).toBe(1);
  });

  it('caches and byte-replays a deterministic 4xx response without rerunning the handler', async () => {
    const operationId = '33333333-3333-4333-8333-333333333333';
    const first = await postJson(operationId, '{"fail":true}');
    const replay = await postJson(operationId, '{"fail":true}');

    expect(first.status).toBe(400);
    expect(replay.status).toBe(400);
    expect(replay.body).toBe(first.body);
    expect(controller.calls).toBe(1);
  });

  async function postJson(operationId: string, body: string) {
    const response = await fetch(`${baseUrl}/v1/idempotency-v2-test`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Internal-Auth': await signToken(),
        'Idempotency-Key': operationId,
      },
      body,
    });
    return { status: response.status, body: await response.text() };
  }
});

async function signToken(): Promise<string> {
  const token = await new SignJWT({ sub: USER_ID, role: 'SYSTEM_ADMIN' })
    .setProtectedHeader({ alg: 'HS256', typ: 'JWT' })
    .setIssuer('vietride-gateway')
    .setAudience('vietride-internal')
    .setIssuedAt()
    .setExpirationTime('120s')
    .sign(new TextEncoder().encode(SECRET));
  return `Bearer ${token}`;
}
