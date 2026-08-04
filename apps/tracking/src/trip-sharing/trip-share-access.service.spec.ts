import {
  HttpException,
  HttpStatus,
  NotFoundException,
  ServiceUnavailableException,
  UnauthorizedException,
} from '@nestjs/common';
import { TripShareGrantRepository } from './trip-share-grant.repository';
import { TripShareAccessService } from './trip-share-access.service';
import { TripShareRateLimiter } from './trip-share-rate-limiter';
import { TripShareTokenCodec } from './trip-share-token.codec';
import { TripShareTripSnapshotProvider } from './trip-share-trip-snapshot.provider';

const GRANT_ID = '11111111-1111-4111-8111-111111111111';
const TRIP_ID = '22222222-2222-4222-8222-222222222222';
const USER_ID = '33333333-3333-4333-8333-333333333333';
const TOKEN = `v1.${GRANT_ID}.valid-signature`;
const TOKEN_HASH = 'a'.repeat(64);
const NOW = new Date('2026-08-03T10:00:00.000Z');
const EXPIRES_AT = new Date('2026-08-04T10:00:00.000Z');

describe('TripShareAccessService', () => {
  const repository = {
    findById: jest.fn(),
    revokeGrantById: jest.fn(),
  };
  const codec = { verify: jest.fn() };
  const rateLimiter = { consume: jest.fn() };
  const tripProvider = { getTrip: jest.fn() };
  let service: TripShareAccessService;

  beforeEach(() => {
    jest.clearAllMocks();
    service = new TripShareAccessService(
      repository as unknown as TripShareGrantRepository,
      codec as unknown as TripShareTokenCodec,
      rateLimiter as unknown as TripShareRateLimiter,
      tripProvider as unknown as TripShareTripSnapshotProvider,
    );
    rateLimiter.consume.mockResolvedValue(undefined);
    codec.verify.mockReturnValue({ version: 'v1', grantId: GRANT_ID, tokenHash: TOKEN_HASH });
    repository.findById.mockResolvedValue(createGrant());
    repository.revokeGrantById.mockResolvedValue(1);
    tripProvider.getTrip.mockResolvedValue({ tripId: TRIP_ID, status: 'IN_PROGRESS' });
  });

  it('rejects a missing token without consuming the limiter', async () => {
    await expect(service.authorize(undefined, NOW)).rejects.toMatchObject({
      status: HttpStatus.UNAUTHORIZED,
      response: { errorCode: 'TRACKING_SHARE_TOKEN_INVALID' },
    });
    expect(rateLimiter.consume).not.toHaveBeenCalled();
  });

  it.each(['malformed', 'tampered'])('rate-limits before rejecting a %s token', async () => {
    codec.verify.mockImplementationOnce(() => {
      throw invalidToken();
    });

    await expect(service.authorize(TOKEN, NOW)).rejects.toMatchObject({
      status: HttpStatus.UNAUTHORIZED,
    });
    expect(rateLimiter.consume).toHaveBeenCalledWith('context', TOKEN);
    expect(rateLimiter.consume.mock.invocationCallOrder[0]).toBeLessThan(
      codec.verify.mock.invocationCallOrder[0] as number,
    );
    expect(repository.findById).not.toHaveBeenCalled();
  });

  it('maps a missing grant and token-hash mismatch to the same 401 contract', async () => {
    repository.findById.mockResolvedValueOnce(null);
    await expect(service.authorize(TOKEN, NOW)).rejects.toMatchObject({
      status: HttpStatus.UNAUTHORIZED,
      response: { errorCode: 'TRACKING_SHARE_TOKEN_INVALID' },
    });

    repository.findById.mockResolvedValueOnce(createGrant({ tokenHash: 'b'.repeat(64) }));
    await expect(service.authorize(TOKEN, NOW)).rejects.toMatchObject({
      status: HttpStatus.UNAUTHORIZED,
      response: { errorCode: 'TRACKING_SHARE_TOKEN_INVALID' },
    });
  });

  it('compares malformed stored hashes without throwing from timingSafeEqual', async () => {
    repository.findById.mockResolvedValueOnce(createGrant({ tokenHash: 'abc' }));
    await expect(service.authorize(TOKEN, NOW)).rejects.toMatchObject({
      status: HttpStatus.UNAUTHORIZED,
      response: { errorCode: 'TRACKING_SHARE_TOKEN_INVALID' },
    });
  });

  it('returns only the internal access context for a valid active grant', async () => {
    await expect(service.authorize(TOKEN, NOW)).resolves.toEqual({
      grantId: GRANT_ID,
      tripId: TRIP_ID,
      expiresAt: EXPIRES_AT,
      status: 'IN_PROGRESS',
    });
  });

  it('uses the socket limiter for an initial socket authorization', async () => {
    await service.authorizeSocket(TOKEN, NOW);

    expect(rateLimiter.consume).toHaveBeenCalledWith('socket', TOKEN);
  });

  it('revalidates without consuming any rate limiter quota', async () => {
    await service.revalidate(TOKEN, NOW);

    expect(rateLimiter.consume).not.toHaveBeenCalled();
    expect(repository.findById).toHaveBeenCalledWith(GRANT_ID);
    expect(tripProvider.getTrip).toHaveBeenCalledWith(TRIP_ID);
  });

  it('maps revoked grants to 410 without consulting Trip', async () => {
    repository.findById.mockResolvedValueOnce(
      createGrant({ revokedAt: NOW, revokeReason: 'USER_REVOKED' }),
    );
    await expectUnavailable();
    expect(tripProvider.getTrip).not.toHaveBeenCalled();
  });

  it('preserves EXPIRED when revalidating an already expired grant', async () => {
    repository.findById.mockResolvedValueOnce(
      createGrant({ revokedAt: NOW, revokeReason: 'EXPIRED' }),
    );

    await expect(service.revalidate(TOKEN, NOW)).rejects.toMatchObject({
      status: HttpStatus.GONE,
      response: { errorCode: 'TRACKING_SHARE_LINK_UNAVAILABLE' },
      cause: 'EXPIRED',
    });
    expect(tripProvider.getTrip).not.toHaveBeenCalled();
  });

  it('lazily expires an active grant and maps it to 410', async () => {
    repository.findById.mockResolvedValueOnce(createGrant({ expiresAt: NOW }));
    await expect(service.revalidate(TOKEN, NOW)).rejects.toMatchObject({
      status: HttpStatus.GONE,
      response: { errorCode: 'TRACKING_SHARE_LINK_UNAVAILABLE' },
      cause: 'EXPIRED',
    });
    expect(repository.revokeGrantById).toHaveBeenCalledWith(GRANT_ID, 'EXPIRED', NOW);
  });

  it.each(['COMPLETED', 'CANCELLED', 'DISRUPTED'])(
    'revokes a %s Trip grant and returns 410',
    async (status) => {
      tripProvider.getTrip.mockResolvedValueOnce({ tripId: TRIP_ID, status });
      await expectUnavailable();
      expect(repository.revokeGrantById).toHaveBeenCalledWith(GRANT_ID, 'TRIP_TERMINATED', NOW);
    },
  );

  it('maps a Trip 404 after grant validation to 410', async () => {
    tripProvider.getTrip.mockRejectedValueOnce(new NotFoundException());
    await expectUnavailable();
    expect(repository.revokeGrantById).toHaveBeenCalledWith(GRANT_ID, 'TRIP_TERMINATED', NOW);
  });

  it('propagates rate-limit and Redis limiter failures', async () => {
    const rateLimited = new HttpException(
      { errorCode: 'RATE_LIMITED' },
      HttpStatus.TOO_MANY_REQUESTS,
    );
    rateLimiter.consume.mockRejectedValueOnce(rateLimited);
    await expect(service.authorize(TOKEN, NOW)).rejects.toBe(rateLimited);

    const unavailable = new ServiceUnavailableException({
      errorCode: 'TRACKING_SHARE_RATE_LIMIT_UNAVAILABLE',
    });
    rateLimiter.consume.mockRejectedValueOnce(unavailable);
    await expect(service.authorize(TOKEN, NOW)).rejects.toBe(unavailable);
  });

  it('propagates Trip transport or malformed dependency failures as 503', async () => {
    const unavailable = new ServiceUnavailableException({ errorCode: 'TRACKING_TRIP_UNAVAILABLE' });
    tripProvider.getTrip.mockRejectedValueOnce(unavailable);
    await expect(service.authorize(TOKEN, NOW)).rejects.toBe(unavailable);
  });

  async function expectUnavailable(): Promise<void> {
    await expect(service.authorize(TOKEN, NOW)).rejects.toMatchObject({
      status: HttpStatus.GONE,
      response: { errorCode: 'TRACKING_SHARE_LINK_UNAVAILABLE' },
    });
  }
});

function createGrant(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    id: GRANT_ID,
    tripId: TRIP_ID,
    createdByUserId: USER_ID,
    tokenHash: TOKEN_HASH,
    tokenVersion: 1,
    expiresAt: EXPIRES_AT,
    revokedAt: null,
    revokeReason: null,
    createdAt: NOW,
    updatedAt: NOW,
    ...overrides,
  };
}

function invalidToken(): UnauthorizedException {
  return new UnauthorizedException({
    errorCode: 'TRACKING_SHARE_TOKEN_INVALID',
    detail: 'The trip share token is invalid',
  });
}
