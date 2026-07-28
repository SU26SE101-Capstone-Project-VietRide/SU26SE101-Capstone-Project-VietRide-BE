import type { Env } from '../config/env.schema';
import { IdentitySystemAdminRecipientProvider } from './identity-system-admin-recipient.provider';

const FIRST_ADMIN_ID = '11111111-1111-4111-8111-111111111111';
const SECOND_ADMIN_ID = '22222222-2222-4222-8222-222222222222';

describe('IdentitySystemAdminRecipientProvider', () => {
  const originalFetch = global.fetch;

  afterEach(() => {
    global.fetch = originalFetch;
  });

  it('returns deterministic distinct System Admin recipients', async () => {
    global.fetch = jest
      .fn()
      .mockResolvedValue(jsonResponse([FIRST_ADMIN_ID, SECOND_ADMIN_ID, FIRST_ADMIN_ID]));

    const provider = new IdentitySystemAdminRecipientProvider(createEnv());

    await expect(provider.resolveSystemAdminRecipientUserIds()).resolves.toEqual([
      FIRST_ADMIN_ID,
      SECOND_ADMIN_ID,
    ]);
    const url = (global.fetch as jest.Mock).mock.calls[0]?.[0] as URL;
    expect(url.pathname).toBe('/internal/v1/users/system-admin-recipient-ids');
  });

  it.each([
    ['non-success', new Response(null, { status: 503 })],
    ['malformed', jsonResponse([{ userId: FIRST_ADMIN_ID }])],
  ])('fails closed for a %s Identity response', async (_caseName, response) => {
    global.fetch = jest.fn().mockResolvedValue(response);

    await expect(
      new IdentitySystemAdminRecipientProvider(createEnv()).resolveSystemAdminRecipientUserIds(),
    ).rejects.toThrow();
  });
});

function createEnv(): Env {
  return {
    IDENTITY_INTERNAL_BASE_URL: 'http://identity.test',
    INTERNAL_JWT_SECRET: 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa',
    INTERNAL_JWT_TTL_SEC: 120,
  } as Env;
}

function jsonResponse(value: unknown): Response {
  return new Response(JSON.stringify(value), {
    status: 200,
    headers: { 'content-type': 'application/json' },
  });
}
