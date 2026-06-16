import { ExecutionContext, UnauthorizedException } from '@nestjs/common';
import { SignJWT } from 'jose';
import type { Env } from '../config/env.schema';
import { InternalJwtAuthGuard } from './internal-jwt-auth.guard';
import type { RequestWithRagInternalUser } from './rag-internal-user.types';

const INTERNAL_JWT_SECRET = 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa';
const INTERNAL_JWT_ISSUER = 'vietride-gateway';
const INTERNAL_JWT_AUDIENCE = 'vietride-internal';

describe('InternalJwtAuthGuard (e2e)', () => {
  let guard: InternalJwtAuthGuard;

  beforeEach(() => {
    guard = new InternalJwtAuthGuard({
      INTERNAL_JWT_SECRET,
    } as Env);
  });

  it('rejects missing internal auth header', async () => {
    await expect(guard.canActivate(makeContext({}))).rejects.toBeInstanceOf(UnauthorizedException);
  });

  it('rejects invalid internal auth token', async () => {
    await expect(
      guard.canActivate(makeContext({ 'x-internal-auth': 'Bearer invalid-token' })),
    ).rejects.toBeInstanceOf(UnauthorizedException);
  });

  it('accepts valid gateway internal JWT and attaches user context', async () => {
    const request: RequestWithRagInternalUser = { headers: {} } as RequestWithRagInternalUser;
    request.headers['x-internal-auth'] = await signInternalJwt({
      sub: '11111111-1111-1111-1111-111111111111',
      role: 'OPERATOR_ADMIN',
      operatorId: '22222222-2222-2222-2222-222222222222',
      reqId: 'req-rag',
    });

    await expect(guard.canActivate(makeContextFromRequest(request))).resolves.toBe(true);

    expect(request.user).toEqual({
      sub: '11111111-1111-1111-1111-111111111111',
      role: 'OPERATOR_ADMIN',
      operatorId: '22222222-2222-2222-2222-222222222222',
      reqId: 'req-rag',
    });
  });
});

function makeContext(headers: Record<string, string>): ExecutionContext {
  return makeContextFromRequest({ headers } as RequestWithRagInternalUser);
}

function makeContextFromRequest(request: RequestWithRagInternalUser): ExecutionContext {
  return {
    switchToHttp: () => ({
      getRequest: () => request,
    }),
  } as ExecutionContext;
}

async function signInternalJwt(payload: Record<string, string>): Promise<string> {
  const token = await new SignJWT(payload)
    .setProtectedHeader({ alg: 'HS256', typ: 'JWT' })
    .setIssuer(INTERNAL_JWT_ISSUER)
    .setAudience(INTERNAL_JWT_AUDIENCE)
    .setIssuedAt()
    .setExpirationTime('120s')
    .sign(new TextEncoder().encode(INTERNAL_JWT_SECRET));

  return `Bearer ${token}`;
}
