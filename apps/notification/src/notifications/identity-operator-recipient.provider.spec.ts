import type { Env } from '../config/env.schema';
import { IdentityOperatorRecipientProvider } from './identity-operator-recipient.provider';

const FIRST_USER_ID = '11111111-1111-4111-8111-111111111111';
const SECOND_USER_ID = '22222222-2222-4222-8222-222222222222';
const OPERATOR_ID = '33333333-3333-4333-8333-333333333333';

describe('IdentityOperatorRecipientProvider', () => {
  const originalFetch = global.fetch;

  afterEach(() => {
    global.fetch = originalFetch;
  });

  it('loads recipient emails in one authenticated internal batch request', async () => {
    global.fetch = jest.fn().mockResolvedValue(
      new Response(
        JSON.stringify([
          {
            id: FIRST_USER_ID,
            email: 'first@vietride.local',
            role: 'OPERATOR_ADMIN',
            status: 'ACTIVE',
            operatorId: OPERATOR_ID,
          },
          {
            id: SECOND_USER_ID,
            email: 'second@vietride.local',
            role: 'OPERATOR_STAFF',
            status: 'ACTIVE',
            operatorId: OPERATOR_ID,
          },
        ]),
        { status: 200, headers: { 'content-type': 'application/json' } },
      ),
    );
    const provider = new IdentityOperatorRecipientProvider(createEnv());

    await expect(
      provider.resolveOperatorRecipientEmails(OPERATOR_ID, [
        FIRST_USER_ID,
        SECOND_USER_ID,
        FIRST_USER_ID,
      ]),
    ).resolves.toEqual([{ userId: FIRST_USER_ID, email: 'first@vietride.local' }]);

    const requestUrl = (global.fetch as jest.Mock).mock.calls[0]?.[0] as URL;
    expect(requestUrl.pathname).toBe('/internal/v1/users');
    expect(requestUrl.searchParams.getAll('ids')).toEqual([FIRST_USER_ID, SECOND_USER_ID]);
    expect((global.fetch as jest.Mock).mock.calls[0]?.[1].headers['X-Internal-Auth']).toMatch(
      /^Bearer /,
    );
  });
});

function createEnv(): Env {
  return {
    IDENTITY_INTERNAL_BASE_URL: 'http://identity.test',
    INTERNAL_JWT_SECRET: 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa',
    INTERNAL_JWT_TTL_SEC: 120,
  } as Env;
}
