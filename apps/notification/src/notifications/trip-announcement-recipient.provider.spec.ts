import type { Env } from '../config/env.schema';
import { TripAnnouncementRecipientProvider } from './trip-announcement-recipient.provider';

const TRIP_ID = '11111111-1111-4111-8111-111111111111';
const OPERATOR_ID = '22222222-2222-4222-8222-222222222222';
const DRIVER_ID = '33333333-3333-4333-8333-333333333333';
const ASSISTANT_ID = '44444444-4444-4444-8444-444444444444';

describe('TripAnnouncementRecipientProvider', () => {
  const originalFetch = global.fetch;

  afterEach(() => {
    global.fetch = originalFetch;
  });

  it('returns only the assigned Assistant when the operator matches', async () => {
    global.fetch = jest.fn().mockResolvedValue(
      jsonResponse({
        operatorId: OPERATOR_ID,
        driverUserId: DRIVER_ID,
        assistantUserId: ASSISTANT_ID,
      }),
    );

    await expect(
      new TripAnnouncementRecipientProvider(createEnv()).resolveTripAssistantUserId(
        TRIP_ID,
        OPERATOR_ID,
      ),
    ).resolves.toBe(ASSISTANT_ID);
  });

  it('returns current crew only when the operator matches', async () => {
    global.fetch = jest.fn().mockResolvedValue(
      jsonResponse({ operatorId: OPERATOR_ID, driverUserId: DRIVER_ID, assistantUserId: null }),
    );

    await expect(
      new TripAnnouncementRecipientProvider(createEnv()).resolveTripCrewUserIds(
        TRIP_ID,
        OPERATOR_ID,
      ),
    ).resolves.toEqual([DRIVER_ID]);
  });

  it.each([
    ['non-success', new Response(null, { status: 503 })],
    ['malformed', jsonResponse({ operatorId: OPERATOR_ID, driverUserId: 'invalid' })],
  ])('fails closed for a %s Trip response', async (_caseName, response) => {
    global.fetch = jest.fn().mockResolvedValue(response);

    await expect(
      new TripAnnouncementRecipientProvider(createEnv()).resolveTripCrewUserIds(
        TRIP_ID,
        OPERATOR_ID,
      ),
    ).rejects.toThrow();
  });
});

function createEnv(): Env {
  return {
    TRIP_INTERNAL_BASE_URL: 'http://trip.test',
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
