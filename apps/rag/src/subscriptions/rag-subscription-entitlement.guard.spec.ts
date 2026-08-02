import {
  Controller,
  ForbiddenException,
  Get,
  type ExecutionContext,
  type INestApplication,
  UseGuards,
} from '@nestjs/common';
import { APP_FILTER } from '@nestjs/core';
import { Test } from '@nestjs/testing';
import { ApiResponseExceptionFilter } from '@vietride/nest-common';
import { SignJWT } from 'jose';
import type { AddressInfo } from 'node:net';
import { ENV_TOKEN } from '../app/tokens';
import { InternalJwtAuthGuard } from '../auth/internal-jwt-auth.guard';
import type { RequestWithRagInternalUser } from '../auth/rag-internal-user.types';
import type { Env } from '../config/env.schema';
import { IdentitySubscriptionEntitlementClient } from './identity-subscription-entitlement.client';
import { RagSubscriptionEntitlementGuard } from './rag-subscription-entitlement.guard';

const SECRET = 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa';
const USER_ID = '11111111-1111-4111-8111-111111111111';
const OPERATOR_ID = '22222222-2222-4222-8222-222222222222';
const BODY_OPERATOR_ID = '33333333-3333-4333-8333-333333333333';
let httpSideEffect: jest.Mock;

@Controller('entitlement-test')
@UseGuards(InternalJwtAuthGuard, RagSubscriptionEntitlementGuard)
class EntitlementTestController {
  @Get()
  execute(): { reached: boolean } {
    httpSideEffect();
    return { reached: true };
  }
}

describe('RagSubscriptionEntitlementGuard', () => {
  let subscriptions: jest.Mocked<IdentitySubscriptionEntitlementClient>;
  let guard: RagSubscriptionEntitlementGuard;
  let authGuard: InternalJwtAuthGuard;
  let downstreamSideEffect: jest.Mock;

  beforeEach(() => {
    subscriptions = {
      get: jest.fn().mockResolvedValue({
        operatorId: OPERATOR_ID,
        status: 'ACTIVE',
        enableRag: true,
      }),
    } as unknown as jest.Mocked<IdentitySubscriptionEntitlementClient>;
    guard = new RagSubscriptionEntitlementGuard(subscriptions);
    authGuard = new InternalJwtAuthGuard(env());
    downstreamSideEffect = jest.fn();
  });

  it('uses only the operator tenant from the verified Internal JWT and caches per request', async () => {
    const request = await signedRequest('OPERATOR_ADMIN', OPERATOR_ID);
    request.body = { operatorId: BODY_OPERATOR_ID };
    request.query = { operatorId: BODY_OPERATOR_ID };
    const context = executionContext(request);

    await authGuard.canActivate(context);
    await guard.canActivate(context);
    await guard.canActivate(context);
    downstreamSideEffect();

    expect(request.user).toMatchObject({
      sub: USER_ID,
      role: 'OPERATOR_ADMIN',
      operatorId: OPERATOR_ID,
    });
    expect(subscriptions.get).toHaveBeenCalledTimes(1);
    expect(subscriptions.get).toHaveBeenCalledWith(OPERATOR_ID);
    expect(downstreamSideEffect).toHaveBeenCalledTimes(1);
  });

  it('blocks a disabled module before repository or provider side effects', async () => {
    subscriptions.get.mockResolvedValue({
      operatorId: OPERATOR_ID,
      status: 'PENDING_PAYMENT',
      enableRag: false,
    });
    const context = executionContext(await signedRequest('DRIVER', OPERATOR_ID));

    await authGuard.canActivate(context);
    const action = guard.canActivate(context).then(() => downstreamSideEffect());

    await expectForbidden(action, 'SUBSCRIPTION_MODULE_DISABLED');
    expect(downstreamSideEffect).not.toHaveBeenCalled();
  });

  it.each([undefined, 'not-a-uuid'])(
    'rejects an operator-scoped role with missing or invalid verified tenant %s',
    async (operatorId) => {
      const context = executionContext(await signedRequest('ASSISTANT', operatorId));

      await authGuard.canActivate(context);
      const action = guard.canActivate(context).then(() => downstreamSideEffect());

      await expectForbidden(action, 'FORBIDDEN');
      expect(subscriptions.get).not.toHaveBeenCalled();
      expect(downstreamSideEffect).not.toHaveBeenCalled();
    },
  );

  it.each([
    ['SYSTEM_ADMIN', BODY_OPERATOR_ID],
    ['PASSENGER', undefined],
  ])(
    'keeps %s behavior unchanged without an Identity entitlement call',
    async (role, operatorId) => {
      const context = executionContext(await signedRequest(role, operatorId));

      await authGuard.canActivate(context);
      await guard.canActivate(context);
      downstreamSideEffect();

      expect(subscriptions.get).not.toHaveBeenCalled();
      expect(downstreamSideEffect).toHaveBeenCalledTimes(1);
    },
  );
});

describe('RagSubscriptionEntitlementGuard HTTP boundary', () => {
  let app: INestApplication;
  let baseUrl: string;
  const subscriptions = {
    get: jest.fn(),
  };

  beforeAll(async () => {
    const moduleRef = await Test.createTestingModule({
      controllers: [EntitlementTestController],
      providers: [
        InternalJwtAuthGuard,
        RagSubscriptionEntitlementGuard,
        { provide: IdentitySubscriptionEntitlementClient, useValue: subscriptions },
        { provide: ENV_TOKEN, useValue: env() },
        { provide: APP_FILTER, useValue: new ApiResponseExceptionFilter() },
      ],
    }).compile();
    app = moduleRef.createNestApplication();
    await app.listen(0, '127.0.0.1');
    baseUrl = `http://127.0.0.1:${(app.getHttpServer().address() as AddressInfo).port}`;
  });

  afterAll(async () => app.close());

  beforeEach(() => {
    jest.clearAllMocks();
    httpSideEffect = jest.fn();
  });

  it('returns the ADR 0004 module-disabled envelope before controller side effects', async () => {
    subscriptions.get.mockResolvedValue({
      operatorId: OPERATOR_ID,
      status: 'PENDING_PAYMENT',
      enableRag: false,
    });
    const request = await signedRequest('OPERATOR_ADMIN', OPERATOR_ID);

    const response = await fetch(`${baseUrl}/entitlement-test`, {
      headers: {
        ...(request.headers as Record<string, string>),
        'x-request-id': 'rag-entitlement-test-request',
      },
    });
    const body = (await response.json()) as {
      success: boolean;
      statusCode: number;
      error: { code: string };
      meta: { traceId: string };
    };

    expect(response.status).toBe(403);
    expect(body).toMatchObject({
      success: false,
      statusCode: 403,
      error: { code: 'SUBSCRIPTION_MODULE_DISABLED' },
      meta: { traceId: 'rag-entitlement-test-request' },
    });
    expect(httpSideEffect).not.toHaveBeenCalled();
  });
});

async function signedRequest(
  role: string,
  operatorId: string | undefined,
): Promise<RequestWithRagInternalUser> {
  const token = await new SignJWT({ role, ...(operatorId ? { operatorId } : {}) })
    .setProtectedHeader({ alg: 'HS256', typ: 'JWT' })
    .setSubject(USER_ID)
    .setIssuer('vietride-gateway')
    .setAudience('vietride-internal')
    .setIssuedAt()
    .setExpirationTime('2m')
    .sign(new TextEncoder().encode(SECRET));
  return {
    headers: { 'x-internal-auth': `Bearer ${token}` },
  } as unknown as RequestWithRagInternalUser;
}

function executionContext(request: RequestWithRagInternalUser): ExecutionContext {
  return {
    switchToHttp: () => ({ getRequest: () => request }),
  } as unknown as ExecutionContext;
}

async function expectForbidden(action: Promise<unknown>, errorCode: string): Promise<void> {
  try {
    await action;
    throw new Error('Expected entitlement guard to reject the request');
  } catch (error) {
    expect(error).toBeInstanceOf(ForbiddenException);
    expect((error as ForbiddenException).getResponse()).toMatchObject({ errorCode });
  }
}

function env(): Env {
  return { INTERNAL_JWT_SECRET: SECRET } as Env;
}
