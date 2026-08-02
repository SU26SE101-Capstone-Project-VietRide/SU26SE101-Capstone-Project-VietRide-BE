import { INestApplication } from '@nestjs/common';
import { APP_INTERCEPTOR } from '@nestjs/core';
import { Test } from '@nestjs/testing';
import { SignJWT } from 'jose';
import { RedisService } from '@vietride/nest-redis';
import type { AddressInfo } from 'node:net';
import { ENV_TOKEN } from '../app/tokens';
import { InternalJwtAuthGuard } from '../auth/internal-jwt-auth.guard';
import { ChatController } from '../chat/chat.controller';
import { ChatService } from '../chat/chat.service';
import { RagPrismaService } from '../prisma/rag-prisma.service';
import { IdentitySubscriptionEntitlementClient } from '../subscriptions/identity-subscription-entitlement.client';
import { RagSubscriptionEntitlementGuard } from '../subscriptions/rag-subscription-entitlement.guard';
import { RagIdempotencyInterceptor } from './rag-idempotency.interceptor';
import { RagIdempotencyService } from './rag-idempotency.service';

const SECRET = 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa';
const USER_ID = '11111111-1111-4111-8111-111111111111';
const OPERATION_ID = '22222222-2222-4222-8222-222222222222';

describe('RAG chat idempotency replay (e2e)', () => {
  let app: INestApplication;
  let baseUrl: string;
  const values = new Map<string, string>();
  const chatService = {
    prepareChat: jest.fn(async () => ({ conversationId: 'conversation' })),
    streamPrepared: jest.fn(() => streamEvents()),
  };

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
        if (keyCount === 2 && keys[1]) values.set(keys[1], scriptArgs[1] ?? '');
        values.delete(processingKey);
        return 1;
      }),
    };
    const moduleRef = await Test.createTestingModule({
      controllers: [ChatController],
      providers: [
        InternalJwtAuthGuard,
        RagSubscriptionEntitlementGuard,
        {
          provide: IdentitySubscriptionEntitlementClient,
          useValue: { get: jest.fn().mockResolvedValue({ enableRag: true }) },
        },
        RagIdempotencyService,
        { provide: ChatService, useValue: chatService },
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
        { provide: APP_INTERCEPTOR, useClass: RagIdempotencyInterceptor },
      ],
    }).compile();

    app = moduleRef.createNestApplication();
    await app.listen(0);
    const address = app.getHttpServer().address() as AddressInfo;
    baseUrl = `http://127.0.0.1:${address.port}`;
  });

  afterAll(async () => app.close());

  it('reconnects with the same UUID and replays the captured SSE without running chat twice', async () => {
    const headers = {
      'Content-Type': 'application/json',
      'X-Internal-Auth': await signToken(),
      'Idempotency-Key': OPERATION_ID,
    };
    const body = JSON.stringify({ message: 'Xin chào' });

    const first = await fetch(`${baseUrl}/v1/rag/chat`, { method: 'POST', headers, body });
    const firstBody = await first.text();
    const replay = await fetch(`${baseUrl}/v1/rag/chat`, { method: 'POST', headers, body });
    const replayBody = await replay.text();

    expect(first.status).toBe(200);
    expect(replay.status).toBe(200);
    expect(replayBody).toBe(firstBody);
    expect(replayBody).toContain('event: done');
    expect(chatService.prepareChat).toHaveBeenCalledTimes(1);
    expect(chatService.streamPrepared).toHaveBeenCalledTimes(1);
  });
});

async function signToken(): Promise<string> {
  const token = await new SignJWT({ sub: USER_ID, role: 'PASSENGER', reqId: OPERATION_ID })
    .setProtectedHeader({ alg: 'HS256', typ: 'JWT' })
    .setIssuer('vietride-gateway')
    .setAudience('vietride-internal')
    .setIssuedAt()
    .setExpirationTime('120s')
    .sign(new TextEncoder().encode(SECRET));
  return `Bearer ${token}`;
}

async function* streamEvents() {
  yield { event: 'token' as const, data: { token: 'Xin chào' } };
  yield { event: 'done' as const, data: { conversationId: 'conversation' } };
}
