import {
  ForbiddenException,
  NotFoundException,
  ServiceUnavailableException,
} from '@nestjs/common';
import type { Env } from '../config/env.schema';
import type { TrackingInternalJwtSigner } from '../authorization/tracking-internal-jwt.signer';
import { BookingOwnerAuthorizationProvider } from './booking-owner-authorization.provider';
import { TripShareTripSnapshotProvider } from './trip-share-trip-snapshot.provider';

const TRIP_ID = '11111111-1111-4111-8111-111111111111';
const USER_ID = '22222222-2222-4222-8222-222222222222';
const originalFetch = global.fetch;

describe('Trip share owner HTTP providers', () => {
  const signer = { sign: jest.fn().mockResolvedValue('internal-jwt') } as unknown as jest.Mocked<TrackingInternalJwtSigner>;
  const env = {
    INTERNAL_JWT_SECRET: 'test-secret-at-least-32-characters-long',
    INTERNAL_JWT_TTL_SEC: 120,
    BOOKING_SERVICE_BASE_URL: 'http://booking.test',
    BOOKING_TRACKING_AUTH_PATH: '/internal/v1/trips/:tripId/tracking-authorization/bookings',
    TRIP_SERVICE_BASE_URL: 'http://trip.test',
    TRACKING_AUTH_HTTP_TIMEOUT_MS: 20,
    TRACKING_DATA_PROVIDER_TIMEOUT_MS: 20,
  } as Env;

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe('BookingOwnerAuthorizationProvider', () => {
    it.each([
      { allowed: true, scope: 'BOOKING_OWNER' },
      { success: true, data: { allowed: true, scope: 'BOOKING_OWNER' } },
    ])('accepts direct and envelope BOOKING_OWNER responses', async (body) => {
      global.fetch = jest.fn().mockResolvedValue(jsonResponse(200, body));
      const provider = new BookingOwnerAuthorizationProvider(env, signer);

      await expect(provider.requireBookingOwner(USER_ID, TRIP_ID)).resolves.toBeUndefined();
      expect(global.fetch).toHaveBeenCalledWith(
        expect.stringContaining(`/internal/v1/trips/${TRIP_ID}/tracking-authorization/bookings?userId=${USER_ID}&role=PASSENGER`),
        expect.objectContaining({ headers: { 'X-Internal-Auth': 'Bearer internal-jwt' } }),
      );
    });

    it.each([
      [200, { allowed: false }],
      [200, { allowed: true, scope: 'PARCEL_RECIPIENT' }],
      [200, { success: false, error: { code: 'ACCESS_DENIED' } }],
      [403, { allowed: false }],
      [404, null],
    ])('maps denial, wrong scope, 403 and 404 to ACCESS_DENIED', async (status, body) => {
      global.fetch = jest.fn().mockResolvedValue(jsonResponse(status, body));
      const provider = new BookingOwnerAuthorizationProvider(env, signer);
      await expect(provider.requireBookingOwner(USER_ID, TRIP_ID)).rejects.toBeInstanceOf(ForbiddenException);
    });

    it.each([
      [401, null],
      [500, null],
      [200, { unexpected: true }],
    ])('maps unavailable or malformed transport to TRACKING_AUTH_UNAVAILABLE', async (status, body) => {
      global.fetch = jest.fn().mockResolvedValue(jsonResponse(status, body));
      const provider = new BookingOwnerAuthorizationProvider(env, signer);
      await expect(provider.requireBookingOwner(USER_ID, TRIP_ID)).rejects.toBeInstanceOf(ServiceUnavailableException);
    });

    it('maps timeout/network rejection to TRACKING_AUTH_UNAVAILABLE', async () => {
      global.fetch = jest.fn().mockRejectedValue(new Error('timeout'));
      const provider = new BookingOwnerAuthorizationProvider(env, signer);
      await expect(provider.requireBookingOwner(USER_ID, TRIP_ID)).rejects.toBeInstanceOf(ServiceUnavailableException);
    });
  });

  describe('TripShareTripSnapshotProvider', () => {
    it.each([
      { tripId: TRIP_ID, status: 'IN_PROGRESS', extra: true },
      { success: true, data: { tripId: TRIP_ID, status: 'IN_PROGRESS' } },
    ])('accepts a direct or enveloped passthrough Trip snapshot', async (body) => {
      global.fetch = jest.fn().mockResolvedValue(jsonResponse(200, body));
      const provider = new TripShareTripSnapshotProvider(env, signer);
      await expect(provider.getTrip(TRIP_ID)).resolves.toMatchObject({ tripId: TRIP_ID, status: 'IN_PROGRESS' });
    });

    it('maps Trip 404 to TRIP_NOT_FOUND', async () => {
      global.fetch = jest.fn().mockResolvedValue(jsonResponse(404, null));
      const provider = new TripShareTripSnapshotProvider(env, signer);
      await expect(provider.getTrip(TRIP_ID)).rejects.toBeInstanceOf(NotFoundException);
    });

    it.each([
      [401, null],
      [500, null],
      [200, { tripId: 'bad', status: 'IN_PROGRESS' }],
      [200, { tripId: TRIP_ID }],
      [200, { tripId: '33333333-3333-4333-8333-333333333333', status: 'IN_PROGRESS' }],
    ])('maps unavailable, malformed or mismatched snapshots to TRACKING_TRIP_UNAVAILABLE', async (status, body) => {
      global.fetch = jest.fn().mockResolvedValue(jsonResponse(status, body));
      const provider = new TripShareTripSnapshotProvider(env, signer);
      await expect(provider.getTrip(TRIP_ID)).rejects.toBeInstanceOf(ServiceUnavailableException);
    });
  });
});

function jsonResponse(status: number, body: unknown): Response {
  return new Response(body === null ? '' : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}
