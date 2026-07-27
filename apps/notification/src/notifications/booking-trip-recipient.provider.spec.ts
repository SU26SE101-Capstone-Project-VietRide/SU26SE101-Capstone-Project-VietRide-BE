import type { Env } from '../config/env.schema';
import { BookingTripRecipientProvider } from './booking-trip-recipient.provider';

const TRIP_ID = '11111111-1111-4111-8111-111111111111';
const USER_ID = '22222222-2222-4222-8222-222222222222';
const BOOKING_ID = '33333333-3333-4333-8333-333333333333';
const OTHER_BOOKING_ID = '44444444-4444-4444-8444-444444444444';
const OTHER_USER_ID = '55555555-5555-4555-8555-555555555555';

describe('BookingTripRecipientProvider', () => {
  const originalFetch = global.fetch;

  afterEach(() => {
    global.fetch = originalFetch;
  });

  it('returns only passengers whose booking is affected by a route change', async () => {
    global.fetch = jest.fn().mockResolvedValue(
      jsonResponse({
        tripId: TRIP_ID,
        recipients: [
          { bookingId: BOOKING_ID, userId: USER_ID, status: 'CONFIRMED' },
          { bookingId: OTHER_BOOKING_ID, userId: OTHER_USER_ID, status: 'CONFIRMED' },
        ],
      }),
    );

    await expect(
      new BookingTripRecipientProvider(createEnv()).resolveAffectedTripPassengerUserIds(
        TRIP_ID,
        [BOOKING_ID],
      ),
    ).resolves.toEqual([USER_ID]);
  });

  it('returns distinct active passenger ids from the raw Booking projection', async () => {
    global.fetch = jest.fn().mockResolvedValue(
      jsonResponse({
        tripId: TRIP_ID,
        recipients: [
          { bookingId: BOOKING_ID, userId: USER_ID, status: 'CONFIRMED' },
          { bookingId: BOOKING_ID, userId: USER_ID, status: 'PARTIAL_NO_SHOW' },
        ],
      }),
    );

    const provider = new BookingTripRecipientProvider(createEnv());

    await expect(provider.resolveTripPassengerUserIds(TRIP_ID)).resolves.toEqual([USER_ID]);
    const url = (global.fetch as jest.Mock).mock.calls[0]?.[0] as URL;
    expect(url.pathname).toBe(`/internal/v1/bookings/trips/${TRIP_ID}/notification-recipients`);
  });

  it.each([
    ['non-success', new Response(null, { status: 503 })],
    ['malformed', jsonResponse({ tripId: TRIP_ID, recipients: [{ userId: 'invalid' }] })],
  ])('fails closed for a %s Booking response', async (_caseName, response) => {
    global.fetch = jest.fn().mockResolvedValue(response);

    await expect(
      new BookingTripRecipientProvider(createEnv()).resolveTripPassengerUserIds(TRIP_ID),
    ).rejects.toThrow();
  });
});

function createEnv(): Env {
  return {
    BOOKING_INTERNAL_BASE_URL: 'http://booking.test',
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
