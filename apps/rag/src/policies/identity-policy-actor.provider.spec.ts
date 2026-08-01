import { jwtVerify } from 'jose';
import type { Env } from '../config/env.schema';
import { IdentityPolicyActorProvider } from './identity-policy-actor.provider';

const SECRET = 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa';
const USER_ID = '11111111-1111-4111-8111-111111111111';

describe('IdentityPolicyActorProvider', () => {
  const originalFetch = global.fetch;

  afterEach(() => {
    global.fetch = originalFetch;
    jest.restoreAllMocks();
  });

  it('uses the bounded Identity batch route and a valid service JWT', async () => {
    global.fetch = jest.fn().mockResolvedValue(
      new Response(
        JSON.stringify([
          {
            id: USER_ID,
            displayName: 'System Admin',
            email: 'admin@vietride.vn',
            deleted: false,
            role: 'SYSTEM_ADMIN',
          },
        ]),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      ),
    );

    const result = await new IdentityPolicyActorProvider(env()).resolve(USER_ID);

    expect(result).toEqual({ displayName: 'System Admin', email: 'admin@vietride.vn' });
    const [url, init] = (global.fetch as jest.Mock).mock.calls[0] as [URL, RequestInit];
    expect(url.pathname).toBe('/internal/v1/users');
    expect(url.searchParams.getAll('ids')).toEqual([USER_ID]);
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
  });

  it.each([
    [[{ id: USER_ID, displayName: 'Deleted', email: null, deleted: true }]],
    [[{ id: USER_ID, displayName: 'Missing email', email: null, deleted: false }]],
    [
      [
        { id: USER_ID, displayName: 'Duplicate', email: 'a@vietride.vn', deleted: false },
        { id: USER_ID, displayName: 'Duplicate', email: 'b@vietride.vn', deleted: false },
      ],
    ],
  ])('fails closed for an invalid Identity actor payload', async (payload) => {
    global.fetch = jest.fn().mockResolvedValue(
      new Response(JSON.stringify(payload), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await expect(new IdentityPolicyActorProvider(env()).resolve(USER_ID)).rejects.toThrow(
      'IDENTITY_POLICY_ACTOR_UNAVAILABLE',
    );
  });

  it('fails closed for a non-success Identity response', async () => {
    global.fetch = jest.fn().mockResolvedValue(new Response('', { status: 503 }));

    await expect(new IdentityPolicyActorProvider(env()).resolve(USER_ID)).rejects.toThrow(
      'IDENTITY_POLICY_ACTOR_UNAVAILABLE',
    );
  });
});

function env(): Env {
  return {
    INTERNAL_JWT_SECRET: SECRET,
    INTERNAL_JWT_TTL_SEC: 120,
    IDENTITY_INTERNAL_BASE_URL: 'http://identity.test',
  } as Env;
}
