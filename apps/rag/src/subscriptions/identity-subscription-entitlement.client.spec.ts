import { ServiceUnavailableException } from '@nestjs/common';
import { jwtVerify } from 'jose';
import type { Env } from '../config/env.schema';
import { IdentitySubscriptionEntitlementClient } from './identity-subscription-entitlement.client';

const SECRET = 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa';
const OPERATOR_ID = '11111111-1111-4111-8111-111111111111';
const OTHER_OPERATOR_ID = '22222222-2222-4222-8222-222222222222';

describe('IdentitySubscriptionEntitlementClient', () => {
  const originalFetch = global.fetch;

  afterEach(() => {
    global.fetch = originalFetch;
    jest.restoreAllMocks();
  });

  it.each(['ACTIVE', 'PENDING_PAYMENT'] as const)(
    'accepts %s and signs the Identity request as the RAG service',
    async (status) => {
      global.fetch = jest
        .fn()
        .mockResolvedValue(jsonResponse(subscriptionResponse({ status, enableRag: true })));

      const result = await new IdentitySubscriptionEntitlementClient(env()).get(OPERATOR_ID);

      expect(result).toEqual({ operatorId: OPERATOR_ID, status, enableRag: true });
      const [url, init] = (global.fetch as jest.Mock).mock.calls[0] as [URL, RequestInit];
      expect(url.pathname).toBe(`/internal/v1/operators/${OPERATOR_ID}/subscription`);
      const header = (init.headers as Record<string, string>)['X-Internal-Auth'];
      expect(header).toMatch(/^Bearer /);
      if (!header) throw new Error('Internal auth header was not set');
      const verified = await jwtVerify(
        header.slice('Bearer '.length),
        new TextEncoder().encode(SECRET),
        { issuer: 'vietride-gateway', audience: 'vietride-internal' },
      );
      expect(verified.payload.sub).toBe('rag-service');
      expect(verified.payload.callerService).toBe('rag');
    },
  );

  it('returns a disabled module decision from the active plan', async () => {
    global.fetch = jest
      .fn()
      .mockResolvedValue(
        jsonResponse(subscriptionResponse({ status: 'PENDING_PAYMENT', enableRag: false })),
      );

    await expect(
      new IdentitySubscriptionEntitlementClient(env()).get(OPERATOR_ID),
    ).resolves.toEqual({
      operatorId: OPERATOR_ID,
      status: 'PENDING_PAYMENT',
      enableRag: false,
    });
  });

  it.each([
    ['operator mismatch', subscriptionResponse({ operatorId: OTHER_OPERATOR_ID })],
    ['non-eligible status', subscriptionResponse({ status: 'EXPIRED' })],
    ['malformed response', { operatorId: OPERATOR_ID, status: 'ACTIVE', plan: {} }],
  ])('fails closed for %s', async (_caseName, payload) => {
    global.fetch = jest.fn().mockResolvedValue(jsonResponse(payload));

    await expectUnavailable(new IdentitySubscriptionEntitlementClient(env()).get(OPERATOR_ID));
  });

  it.each([404, 500, 503])('fails closed for Identity HTTP %s', async (status) => {
    global.fetch = jest.fn().mockResolvedValue(new Response('', { status }));

    await expectUnavailable(new IdentitySubscriptionEntitlementClient(env()).get(OPERATOR_ID));
  });

  it('fails closed for timeout or transport failure', async () => {
    global.fetch = jest.fn().mockRejectedValue(new DOMException('Timed out', 'TimeoutError'));

    await expectUnavailable(new IdentitySubscriptionEntitlementClient(env()).get(OPERATOR_ID));
  });
});

function subscriptionResponse(
  overrides: {
    operatorId?: string;
    status?: 'ACTIVE' | 'PENDING_PAYMENT' | 'EXPIRED';
    enableRag?: boolean;
  } = {},
): Record<string, unknown> {
  return {
    operatorId: overrides.operatorId ?? OPERATOR_ID,
    subscriptionId: '33333333-3333-4333-8333-333333333333',
    status: overrides.status ?? 'ACTIVE',
    plan: {
      planId: '44444444-4444-4444-8444-444444444444',
      name: 'Starter',
      limits: {},
      modules: {
        enableParcel: false,
        enableShuttle: false,
        enableRag: overrides.enableRag ?? true,
      },
    },
    usage: {},
  };
}

function jsonResponse(payload: unknown): Response {
  return new Response(JSON.stringify(payload), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  });
}

async function expectUnavailable(action: Promise<unknown>): Promise<void> {
  try {
    await action;
    throw new Error('Expected subscription entitlement lookup to fail');
  } catch (error) {
    expect(error).toBeInstanceOf(ServiceUnavailableException);
    expect((error as ServiceUnavailableException).getResponse()).toMatchObject({
      errorCode: 'UPSTREAM_UNAVAILABLE',
    });
  }
}

function env(): Env {
  return {
    INTERNAL_JWT_SECRET: SECRET,
    INTERNAL_JWT_TTL_SEC: 120,
    IDENTITY_INTERNAL_BASE_URL: 'http://identity.test',
  } as Env;
}
