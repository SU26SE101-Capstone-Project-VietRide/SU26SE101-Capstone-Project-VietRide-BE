import {
  ConflictException,
  ForbiddenException,
  NotFoundException,
  ServiceUnavailableException,
} from '@nestjs/common';
import type { Env } from '../config/env.schema';
import { TripShareGrantRepository } from './trip-share-grant.repository';
import { TripShareGrantService } from './trip-share-grant.service';
import { TripShareIdempotencyService } from './trip-share-idempotency.service';
import { BookingOwnerAuthorizationProvider } from './booking-owner-authorization.provider';
import { TripShareOwnerService } from './trip-share-owner.service';
import { TripShareTripSnapshotProvider } from './trip-share-trip-snapshot.provider';
import { TripShareTokenCodec } from './trip-share-token.codec';
import { TripShareRealtimePublisher } from './trip-share-realtime.publisher';

const TRIP_ID = '11111111-1111-4111-8111-111111111111';
const USER_ID = '22222222-2222-4222-8222-222222222222';
const GRANT_ID = '33333333-3333-4333-8333-333333333333';
const IDEMPOTENCY_KEY = '44444444-4444-4444-8444-444444444444';
const OWNER_PATH = `/v1/tracking/trips/${TRIP_ID}/share-link`;
const EXPIRES_AT = new Date('2026-08-04T09:30:00.000Z');

describe('TripShareOwnerService', () => {
  let booking: jest.Mocked<BookingOwnerAuthorizationProvider>;
  let trips: jest.Mocked<TripShareTripSnapshotProvider>;
  let grants: jest.Mocked<TripShareGrantService>;
  let repository: jest.Mocked<TripShareGrantRepository>;
  let idempotency: jest.Mocked<TripShareIdempotencyService>;
  let realtime: jest.Mocked<TripShareRealtimePublisher>;
  let service: TripShareOwnerService;

  beforeEach(() => {
    booking = { requireBookingOwner: jest.fn().mockResolvedValue(undefined) } as unknown as jest.Mocked<BookingOwnerAuthorizationProvider>;
    trips = { getTrip: jest.fn().mockResolvedValue({ tripId: TRIP_ID, status: 'IN_PROGRESS' }) } as unknown as jest.Mocked<TripShareTripSnapshotProvider>;
    grants = {
      ensureActive: jest.fn().mockResolvedValue({
        grant: {
          id: GRANT_ID,
          tripId: TRIP_ID,
          createdByUserId: USER_ID,
          tokenHash: 'a'.repeat(64),
          tokenVersion: 1,
          expiresAt: EXPIRES_AT,
          revokedAt: null,
          revokeReason: null,
          createdAt: new Date(),
          updatedAt: new Date(),
        },
        token: 'v1.test.signature',
      }),
    } as unknown as jest.Mocked<TripShareGrantService>;
    repository = {
      findActiveByOwnerTrip: jest.fn().mockResolvedValue({ id: GRANT_ID }),
      revokeGrantById: jest.fn().mockResolvedValue(1),
      revokeOwnActiveGrant: jest.fn().mockResolvedValue(1),
      revokeOwnActiveGrantById: jest.fn().mockResolvedValue(true),
    } as unknown as jest.Mocked<TripShareGrantRepository>;
    idempotency = {
      begin: jest.fn().mockResolvedValue({ state: 'acquired', ownerToken: 'lock-owner' }),
      complete: jest.fn().mockResolvedValue(undefined),
      abandon: jest.fn().mockResolvedValue(undefined),
    } as unknown as jest.Mocked<TripShareIdempotencyService>;
    realtime = {
      revokeGrant: jest.fn().mockResolvedValue(undefined),
    } as unknown as jest.Mocked<TripShareRealtimePublisher>;
    const env = {
      TRACKING_SHARE_PAGE_URL: 'https://app.vietride.vn/trip-sharing',
    } as Env;
    const codec = new TripShareTokenCodec({
      TRACKING_SHARE_TOKEN_SECRET: 'phase13-owner-test-secret-at-least-32-bytes',
    } as Env);
    service = new TripShareOwnerService(
      booking,
      trips,
      grants,
      repository,
      idempotency,
      codec,
      env,
      realtime,
    );
  });

  it('authorizes booking ownership before reading Trip status and returns a fragment link', async () => {
    const calls: string[] = [];
    booking.requireBookingOwner.mockImplementation(async () => { calls.push('booking'); });
    trips.getTrip.mockImplementation(async () => { calls.push('trip'); return { tripId: TRIP_ID, status: 'IN_PROGRESS' }; });

    const result = await service.ensureShareLink(USER_ID, TRIP_ID, IDEMPOTENCY_KEY, OWNER_PATH);

    expect(calls.slice(0, 2)).toEqual(['booking', 'trip']);
    expect(result.expiresAt).toBe(EXPIRES_AT.toISOString());
    expect(new URL(result.shareUrl).hash).toMatch(/^#token=v1\./);
    expect(idempotency.complete).toHaveBeenCalledWith('lock-owner', {
      kind: 'SHARE_GRANT',
      grantId: GRANT_ID,
      expiresAt: EXPIRES_AT.toISOString(),
    });
  });

  it('records a safe 403 outcome for replay and never asks Trip when Booking denies', async () => {
    booking.requireBookingOwner.mockRejectedValue(new ForbiddenException({ errorCode: 'ACCESS_DENIED', detail: 'Denied' }));

    await expect(service.ensureShareLink(USER_ID, TRIP_ID, IDEMPOTENCY_KEY, OWNER_PATH)).rejects.toMatchObject({ status: 403 });
    expect(trips.getTrip).not.toHaveBeenCalled();
    expect(idempotency.complete).toHaveBeenCalledWith('lock-owner', {
      kind: 'ERROR', statusCode: 403, errorCode: 'ACCESS_DENIED', detail: 'Denied',
    });
    expect(idempotency.abandon).not.toHaveBeenCalled();
  });

  it('rejects every non-IN_PROGRESS status and records a replayable 409', async () => {
    for (const status of ['SCHEDULED', 'BOARDING', 'COMPLETED', 'CANCELLED', 'DISRUPTED']) {
      trips.getTrip.mockResolvedValueOnce({ tripId: TRIP_ID, status });
      idempotency.begin.mockResolvedValueOnce({ state: 'acquired', ownerToken: `lock-${status}` });
      await expect(service.ensureShareLink(USER_ID, TRIP_ID, IDEMPOTENCY_KEY, OWNER_PATH)).rejects.toBeInstanceOf(ConflictException);
    }
    expect(grants.ensureActive).not.toHaveBeenCalled();
  });

  it('records Trip 404 as a replayable business error', async () => {
    trips.getTrip.mockRejectedValueOnce(new NotFoundException({
      errorCode: 'TRIP_NOT_FOUND',
      detail: 'Trip not found',
    }));
    await expect(service.ensureShareLink(USER_ID, TRIP_ID, IDEMPOTENCY_KEY, OWNER_PATH)).rejects.toBeInstanceOf(NotFoundException);
    expect(idempotency.complete).toHaveBeenCalledWith('lock-owner', {
      kind: 'ERROR', statusCode: 404, errorCode: 'TRIP_NOT_FOUND', detail: 'Trip not found',
    });
  });

  it('rolls back the created grant when the second Trip snapshot is no longer active', async () => {
    trips.getTrip
      .mockResolvedValueOnce({ tripId: TRIP_ID, status: 'IN_PROGRESS' })
      .mockResolvedValueOnce({ tripId: TRIP_ID, status: 'COMPLETED' });

    await expect(service.ensureShareLink(USER_ID, TRIP_ID, IDEMPOTENCY_KEY, OWNER_PATH)).rejects.toBeInstanceOf(ConflictException);
    expect(repository.revokeGrantById).toHaveBeenCalledWith(GRANT_ID, 'CREATION_ROLLBACK', expect.any(Date));
  });

  it('reconstructs a stable link from a safe idempotency replay', async () => {
    idempotency.begin.mockResolvedValue({
      state: 'replay',
      outcome: { kind: 'SHARE_GRANT', grantId: GRANT_ID, expiresAt: EXPIRES_AT.toISOString() },
    });

    const first = await service.ensureShareLink(USER_ID, TRIP_ID, IDEMPOTENCY_KEY, OWNER_PATH);
    const second = await service.ensureShareLink(USER_ID, TRIP_ID, IDEMPOTENCY_KEY, OWNER_PATH);

    expect(second).toEqual(first);
    expect(booking.requireBookingOwner).not.toHaveBeenCalled();
    expect(JSON.stringify(idempotency.begin.mock.results)).not.toContain('shareUrl');
    expect(JSON.stringify(idempotency.begin.mock.results)).not.toContain('v1.');
  });

  it('returns the same active link when a different idempotency key reaches the stable grant', async () => {
    idempotency.begin
      .mockResolvedValueOnce({ state: 'acquired', ownerToken: 'first-lock' })
      .mockResolvedValueOnce({ state: 'acquired', ownerToken: 'second-lock' });
    const first = await service.ensureShareLink(USER_ID, TRIP_ID, IDEMPOTENCY_KEY, OWNER_PATH);
    const second = await service.ensureShareLink(
      USER_ID,
      TRIP_ID,
      '55555555-5555-4555-8555-555555555555',
      OWNER_PATH,
    );
    expect(second).toEqual(first);
    expect(grants.ensureActive).toHaveBeenCalledTimes(2);
  });

  it('rethrows a replayed business error without calling dependencies', async () => {
    idempotency.begin.mockResolvedValue({
      state: 'replay',
      outcome: { kind: 'ERROR', statusCode: 403, errorCode: 'ACCESS_DENIED', detail: 'Denied' },
    });
    await expect(service.ensureShareLink(USER_ID, TRIP_ID, IDEMPOTENCY_KEY, OWNER_PATH)).rejects.toMatchObject({ status: 403 });
    expect(booking.requireBookingOwner).not.toHaveBeenCalled();
  });

  it.each(['booking', 'trip'])('abandons the owned lock when the %s provider is unavailable', async (target) => {
    const error = new ServiceUnavailableException({
      errorCode: target === 'booking' ? 'TRACKING_AUTH_UNAVAILABLE' : 'TRACKING_TRIP_UNAVAILABLE',
    });
    if (target === 'booking') booking.requireBookingOwner.mockRejectedValue(error);
    else trips.getTrip.mockRejectedValue(error);
    await expect(service.ensureShareLink(USER_ID, TRIP_ID, IDEMPOTENCY_KEY, OWNER_PATH)).rejects.toMatchObject({ status: 503 });
    expect(idempotency.abandon).toHaveBeenCalledWith('lock-owner');
    expect(idempotency.complete).not.toHaveBeenCalled();
  });

  it('revokes the exact active JWT owner grant without calling Booking or Trip and replays idempotently', async () => {
    const first = await service.revokeShareLink(USER_ID, TRIP_ID, IDEMPOTENCY_KEY, OWNER_PATH);
    idempotency.begin.mockResolvedValueOnce({ state: 'replay', outcome: { kind: 'REVOKED', revoked: true } });
    const replay = await service.revokeShareLink(USER_ID, TRIP_ID, IDEMPOTENCY_KEY, OWNER_PATH);

    expect(first).toEqual({ revoked: true });
    expect(replay).toEqual(first);
    expect(repository.revokeOwnActiveGrantById).toHaveBeenCalledWith(
      GRANT_ID,
      TRIP_ID,
      USER_ID,
      expect.any(Date),
    );
    expect(repository.revokeOwnActiveGrant).not.toHaveBeenCalled();
    expect(realtime.revokeGrant).toHaveBeenCalledWith(GRANT_ID, 'REVOKED');
    expect(booking.requireBookingOwner).not.toHaveBeenCalled();
    expect(trips.getTrip).not.toHaveBeenCalled();
  });

  it('does not revoke or emit for a replacement grant when the previously read grant loses the race', async () => {
    repository.revokeOwnActiveGrantById.mockResolvedValueOnce(false);

    await expect(service.revokeShareLink(USER_ID, TRIP_ID, IDEMPOTENCY_KEY, OWNER_PATH))
      .resolves.toEqual({ revoked: true });

    expect(repository.revokeOwnActiveGrantById).toHaveBeenCalledWith(
      GRANT_ID,
      TRIP_ID,
      USER_ID,
      expect.any(Date),
    );
    expect(repository.revokeOwnActiveGrant).not.toHaveBeenCalled();
    expect(realtime.revokeGrant).not.toHaveBeenCalled();
  });

  it('keeps DELETE successful when realtime revocation fails after the database commit', async () => {
    realtime.revokeGrant.mockRejectedValueOnce(new Error('socket unavailable'));

    await expect(service.revokeShareLink(USER_ID, TRIP_ID, IDEMPOTENCY_KEY, OWNER_PATH))
      .resolves.toEqual({ revoked: true });
    await Promise.resolve();

    expect(repository.revokeOwnActiveGrantById).toHaveBeenCalledTimes(1);
  });
});
