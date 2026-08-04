import { BadRequestException, ForbiddenException, UnauthorizedException } from '@nestjs/common';
import type { ExecutionContext } from '@nestjs/common';
import type { UserJwtVerifier } from '../auth/user-jwt.verifier';
import { TripShareOwnerJwtGuard } from './trip-share-owner-jwt.guard';

const TRIP_ID = '11111111-1111-4111-8111-111111111111';

describe('TripShareOwnerJwtGuard', () => {
  const verifier = { verify: jest.fn() } as jest.Mocked<UserJwtVerifier>;
  const guard = new TripShareOwnerJwtGuard(verifier);

  beforeEach(() => verifier.verify.mockReset());

  it('requires a bearer token', async () => {
    await expect(guard.canActivate(contextFor({ params: { tripId: TRIP_ID }, headers: {} }))).rejects.toBeInstanceOf(UnauthorizedException);
  });

  it('rejects an invalid token', async () => {
    verifier.verify.mockRejectedValue(new Error('invalid'));
    await expect(guard.canActivate(contextFor({ params: { tripId: TRIP_ID }, headers: { authorization: 'Bearer bad' } }))).rejects.toBeInstanceOf(UnauthorizedException);
  });

  it('requires PASSENGER without calling downstream providers', async () => {
    verifier.verify.mockResolvedValue({ userId: 'user', role: 'DRIVER' });
    await expect(guard.canActivate(contextFor({ params: { tripId: TRIP_ID }, headers: { authorization: 'Bearer token' } }))).rejects.toBeInstanceOf(ForbiddenException);
  });

  it('rejects invalid tripId and attaches the verified passenger for a valid request', async () => {
    verifier.verify.mockResolvedValue({ userId: 'user', role: 'PASSENGER' });
    await expect(guard.canActivate(contextFor({ params: { tripId: 'bad' }, headers: { authorization: 'Bearer token' } }))).rejects.toBeInstanceOf(BadRequestException);

    const request = { params: { tripId: TRIP_ID }, headers: { authorization: 'Bearer token' } } as Record<string, unknown>;
    await expect(guard.canActivate(contextFor(request))).resolves.toBe(true);
    expect(request['trackingUser']).toEqual({ userId: 'user', role: 'PASSENGER' });
  });
});

function contextFor(request: Record<string, unknown>): ExecutionContext {
  return { switchToHttp: () => ({ getRequest: () => request }) } as unknown as ExecutionContext;
}
