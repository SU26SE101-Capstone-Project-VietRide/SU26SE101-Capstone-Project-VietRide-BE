import type { Env } from '../config/env.schema';
import { ParcelRecipientProvider } from './parcel-recipient.provider';

const PARCEL_ID = '11111111-1111-4111-8111-111111111111';
const TRIP_ID = '22222222-2222-4222-8222-222222222222';
const SENDER_ID = '33333333-3333-4333-8333-333333333333';
const RECIPIENT_ID = '44444444-4444-4444-8444-444444444444';
const OPERATOR_ID = '55555555-5555-4555-8555-555555555555';

describe('ParcelRecipientProvider', () => {
  const originalFetch = global.fetch;

  afterEach(() => {
    global.fetch = originalFetch;
  });

  it('parses the ADR envelope and keeps terminal Parcel recipients resolvable', async () => {
    global.fetch = jest.fn().mockResolvedValue(
      jsonResponse({
        success: true,
        statusCode: 200,
        data: {
          parcelId: PARCEL_ID,
          tripId: TRIP_ID,
          status: 'REJECTED',
          senderUserId: SENDER_ID,
          recipientUserId: RECIPIENT_ID,
          operatorId: OPERATOR_ID,
          dropoffStopId: null,
        },
        meta: { traceId: 'trace-1', timestamp: '2026-07-27T10:00:00Z' },
      }),
    );

    const provider = new ParcelRecipientProvider(createEnv());

    await expect(provider.getParcelSnapshot(PARCEL_ID)).resolves.toMatchObject({
      status: 'REJECTED',
      senderUserId: SENDER_ID,
      recipientUserId: RECIPIENT_ID,
      operatorId: OPERATOR_ID,
    });
  });

  it.each([
    ['non-success', new Response(null, { status: 503 })],
    ['malformed', jsonResponse({ success: true, statusCode: 200, data: { parcelId: PARCEL_ID } })],
  ])('fails closed for a %s Parcel response', async (_caseName, response) => {
    global.fetch = jest.fn().mockResolvedValue(response);

    await expect(
      new ParcelRecipientProvider(createEnv()).getParcelSnapshot(PARCEL_ID),
    ).rejects.toThrow();
  });
});

function createEnv(): Env {
  return {
    PARCEL_INTERNAL_BASE_URL: 'http://parcel.test',
    INTERNAL_JWT_SECRET: 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa',
    INTERNAL_JWT_TTL_SEC: 120,
  } as unknown as Env;
}

function jsonResponse(value: unknown): Response {
  return new Response(JSON.stringify(value), {
    status: 200,
    headers: { 'content-type': 'application/json' },
  });
}
