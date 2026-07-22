import type { ExecutionContext } from '@nestjs/common';
import { UnauthorizedException } from '@nestjs/common';
import { SignJWT } from 'jose';
import type { Env } from '../config/env.schema';
import { TrackingInternalJwtGuard } from './tracking-internal-jwt.guard';

const SECRET = 'day-43-internal-jwt-secret-at-least-32-bytes';

describe('TrackingInternalJwtGuard', () => {
  const env = { INTERNAL_JWT_SECRET: SECRET } as Env;
  const guard = new TrackingInternalJwtGuard(env);

  it('accepts a valid Gateway Internal JWT', async () => {
    const token = await signToken();

    await expect(guard.canActivate(contextWithHeader(`Bearer ${token}`))).resolves.toBe(true);
  });

  it('rejects a missing Internal JWT', async () => {
    await expect(guard.canActivate(contextWithHeader(undefined))).rejects.toBeInstanceOf(
      UnauthorizedException,
    );
  });

  it('rejects a token with the wrong issuer', async () => {
    const token = await signToken('untrusted-service');

    await expect(guard.canActivate(contextWithHeader(`Bearer ${token}`))).rejects.toBeInstanceOf(
      UnauthorizedException,
    );
  });
});

function contextWithHeader(value: string | undefined): ExecutionContext {
  return {
    switchToHttp: () => ({
      getRequest: () => ({ headers: { 'x-internal-auth': value } }),
    }),
  } as unknown as ExecutionContext;
}

function signToken(issuer = 'vietride-gateway'): Promise<string> {
  return new SignJWT({ sub: 'identity-service' })
    .setProtectedHeader({ alg: 'HS256' })
    .setIssuer(issuer)
    .setAudience('vietride-internal')
    .setIssuedAt()
    .setExpirationTime('2m')
    .sign(new TextEncoder().encode(SECRET));
}
