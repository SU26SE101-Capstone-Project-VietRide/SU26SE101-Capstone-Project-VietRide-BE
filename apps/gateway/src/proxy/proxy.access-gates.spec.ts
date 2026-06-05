import type { NextFunction, Request, Response } from 'express';
import { createProxyMiddleware } from 'http-proxy-middleware';
import { jwtVerify, SignJWT, type JWTPayload } from 'jose';
import { InternalJwtSigner } from '../auth/internal-jwt.signer';
import { envSchema } from '../config/env.schema';
import { createProxyHandler } from './proxy.middleware';

jest.mock('http-proxy-middleware', () => ({
  createProxyMiddleware: jest.fn(),
}));

jest.mock('../auth/user-jwt.verifier', () => {
  const { jwtVerify } = jest.requireActual('jose') as typeof import('jose');
  const secret = new TextEncoder().encode('user-access-secret-min-32-chars');

  return {
    createUserJwtVerifier: jest.fn(() => ({
      async verifyAuthorizationHeader(auth: string | undefined): Promise<JWTPayload> {
        if (!auth?.toLowerCase().startsWith('bearer ')) {
          throw new Error('Authorization header required');
        }

        const { payload } = await jwtVerify(auth.slice(7).trim(), secret, {
          issuer: 'vietride-identity',
          audience: 'vietride-api',
        });

        return payload;
      },
    })),
  };
});

const env = envSchema.parse({
  INTERNAL_JWT_SECRET: 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa',
});
const userJwtSecret = new TextEncoder().encode('user-access-secret-min-32-chars');
const createProxyMiddlewareMock = jest.mocked(createProxyMiddleware);

type TestResponse = Response & { statusCodeValue?: number; jsonBody?: unknown };

function makeRequest(path: string, headers: Record<string, string> = {}, method = 'POST'): Request {
  const lowerHeaders = Object.fromEntries(
    Object.entries(headers).map(([key, value]) => [key.toLowerCase(), value]),
  );
  return {
    method,
    url: path,
    originalUrl: path,
    headers: lowerHeaders,
    header: (name: string) => lowerHeaders[name.toLowerCase()],
  } as unknown as Request;
}

function makeResponse(): TestResponse {
  return {
    setHeader: jest.fn(),
    status: jest.fn(function status(this: TestResponse, code: number) {
      this.statusCodeValue = code;
      return this;
    }),
    json: jest.fn(function json(this: TestResponse, body: unknown) {
      this.jsonBody = body;
      return this;
    }),
  } as unknown as TestResponse;
}

async function makeAuthorizationHeader(payload: JWTPayload): Promise<string> {
  const token = await new SignJWT(payload)
    .setProtectedHeader({ alg: 'HS256', typ: 'JWT' })
    .setIssuer(env.JWT_ISSUER)
    .setAudience(env.JWT_AUDIENCE)
    .setIssuedAt()
    .setExpirationTime('5m')
    .sign(userJwtSecret);

  return `Bearer ${token}`;
}

async function expectJoseVerifiedClaim(payload: JWTPayload, claim: 'hasPhone'): Promise<string> {
  const authorization = await makeAuthorizationHeader(payload);
  const { payload: verifiedPayload } = await jwtVerify(authorization.slice(7), userJwtSecret, {
    issuer: env.JWT_ISSUER,
    audience: env.JWT_AUDIENCE,
  });

  expect(verifiedPayload[claim]).toBe(payload[claim]);
  return authorization;
}

function arrangeProxyPass(): jest.Mock {
  const upstreamHandler = jest.fn();
  createProxyMiddlewareMock.mockReturnValue(
    upstreamHandler as unknown as ReturnType<typeof createProxyMiddleware>,
  );
  return upstreamHandler;
}

describe('createProxyHandler RBAC and phone-required gates', () => {
  beforeEach(() => {
    createProxyMiddlewareMock.mockReset();
  });

  it('returns 403 FORBIDDEN for a non-admin JWT on a SYSTEM_ADMIN route', async () => {
    const signer = { sign: jest.fn() } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const authorization = await makeAuthorizationHeader({ sub: 'passenger-1', role: 'PASSENGER' });
    const req = makeRequest('/v1/admin/users', {
      authorization,
      'x-request-id': 'req-rbac',
    });
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(res.status).toHaveBeenCalledWith(403);
    expect(res.jsonBody).toMatchObject({
      success: false,
      statusCode: 403,
      error: { code: 'FORBIDDEN' },
      meta: { traceId: 'req-rbac' },
    });
    expect(signer.sign).not.toHaveBeenCalled();
    expect(createProxyMiddlewareMock).not.toHaveBeenCalled();
    expect(next).not.toHaveBeenCalled();
  });

  it.each([
    ['/v1/admin/operators', env.IDENTITY_BASE_URL],
    ['/v1/admin/operators/11111111-1111-1111-1111-111111111111/approve', env.IDENTITY_BASE_URL],
    ['/v1/admin/users', env.IDENTITY_BASE_URL],
    ['/v1/admin/booking-stats/aggregate', env.BOOKING_BASE_URL],
    ['/v1/admin/platform-wallet', env.PAYMENT_BASE_URL],
    [
      '/v1/admin/trip-settlements/11111111-1111-1111-1111-111111111111/settle',
      env.PAYMENT_BASE_URL,
    ],
  ] as const)('routes SYSTEM_ADMIN request %s to %s', async (path, target) => {
    const upstreamHandler = arrangeProxyPass();
    const signer = {
      sign: jest.fn().mockResolvedValue('internal-token'),
    } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const authorization = await makeAuthorizationHeader({ sub: 'admin-1', role: 'SYSTEM_ADMIN' });
    const req = makeRequest(path, { authorization, 'x-request-id': 'req-admin-route' });
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(signer.sign).toHaveBeenCalledWith({
      sub: 'admin-1',
      reqId: 'req-admin-route',
      role: 'SYSTEM_ADMIN',
    });
    expect(createProxyMiddlewareMock).toHaveBeenCalledWith(expect.objectContaining({ target }));
    expect(req.headers['x-internal-auth']).toBe('Bearer internal-token');
    expect(upstreamHandler).toHaveBeenCalledWith(req, res, next);
    expect(res.status).not.toHaveBeenCalled();
  });

  it('returns 403 AUTH_PHONE_REQUIRED for a jose-verified boolean false hasPhone claim', async () => {
    const signer = { sign: jest.fn() } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const authorization = await expectJoseVerifiedClaim(
      { sub: 'passenger-1', role: 'PASSENGER', hasPhone: false },
      'hasPhone',
    );
    const req = makeRequest('/v1/bookings', { authorization, 'x-request-id': 'req-phone-bool' });
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(res.status).toHaveBeenCalledWith(403);
    expect(res.jsonBody).toMatchObject({
      success: false,
      statusCode: 403,
      error: {
        code: 'AUTH_PHONE_REQUIRED',
        message: 'Vui lòng hoàn tất hồ sơ trước khi tiếp tục.',
      },
      meta: { traceId: 'req-phone-bool' },
    });
    expect(signer.sign).not.toHaveBeenCalled();
    expect(createProxyMiddlewareMock).not.toHaveBeenCalled();
    expect(next).not.toHaveBeenCalled();
  });

  it('returns 403 AUTH_PHONE_REQUIRED for a jose-verified string false hasPhone claim', async () => {
    const signer = { sign: jest.fn() } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const authorization = await expectJoseVerifiedClaim(
      { sub: 'passenger-1', role: 'PASSENGER', hasPhone: 'false' },
      'hasPhone',
    );
    const req = makeRequest('/v1/trips', { authorization, 'x-request-id': 'req-phone-string' });
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(res.status).toHaveBeenCalledWith(403);
    expect(res.jsonBody).toMatchObject({
      success: false,
      statusCode: 403,
      error: { code: 'AUTH_PHONE_REQUIRED' },
      meta: { traceId: 'req-phone-string' },
    });
    expect(signer.sign).not.toHaveBeenCalled();
    expect(createProxyMiddlewareMock).not.toHaveBeenCalled();
    expect(next).not.toHaveBeenCalled();
  });

  it('returns 403 AUTH_PHONE_REQUIRED when a PASSENGER token has no hasPhone claim', async () => {
    const signer = { sign: jest.fn() } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const authorization = await makeAuthorizationHeader({ sub: 'passenger-1', role: 'PASSENGER' });
    const req = makeRequest('/v1/rag', { authorization });
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(res.status).toHaveBeenCalledWith(403);
    expect(res.jsonBody).toMatchObject({
      success: false,
      statusCode: 403,
      error: { code: 'AUTH_PHONE_REQUIRED' },
    });
    expect(signer.sign).not.toHaveBeenCalled();
    expect(createProxyMiddlewareMock).not.toHaveBeenCalled();
    expect(next).not.toHaveBeenCalled();
  });

  it.each([true, 'true'] as const)(
    'allows PASSENGER requests when hasPhone=%p',
    async (hasPhone) => {
      const upstreamHandler = arrangeProxyPass();
      const signer = {
        sign: jest.fn().mockResolvedValue('internal-token'),
      } as unknown as InternalJwtSigner;
      const handler = createProxyHandler(env, signer);
      const authorization = await makeAuthorizationHeader({
        sub: 'passenger-1',
        role: 'PASSENGER',
        hasPhone,
      });
      const req = makeRequest('/v1/bookings', { authorization, 'x-request-id': 'req-phone-pass' });
      const res = makeResponse();
      const next = jest.fn() as NextFunction;

      await handler(req, res, next);

      expect(signer.sign).toHaveBeenCalledWith({
        sub: 'passenger-1',
        reqId: 'req-phone-pass',
        role: 'PASSENGER',
      });
      expect(req.headers['x-internal-auth']).toBe('Bearer internal-token');
      expect(upstreamHandler).toHaveBeenCalledWith(req, res, next);
      expect(res.status).not.toHaveBeenCalled();
    },
  );

  it.each([
    ['GET', '/v1/users/me'],
    ['POST', '/v1/users/me/complete-profile'],
    ['POST', '/v1/auth/logout'],
  ] as const)('lets phone-required whitelist pass for %s %s', async (method, path) => {
    const upstreamHandler = arrangeProxyPass();
    const signer = {
      sign: jest.fn().mockResolvedValue('internal-token'),
    } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const authorization = await makeAuthorizationHeader({
      sub: 'passenger-1',
      role: 'PASSENGER',
      hasPhone: false,
    });
    const req = makeRequest(path, { authorization, 'x-request-id': 'req-whitelist' }, method);
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(signer.sign).toHaveBeenCalled();
    expect(req.headers['x-internal-auth']).toBe('Bearer internal-token');
    expect(upstreamHandler).toHaveBeenCalledWith(req, res, next);
    expect(res.status).not.toHaveBeenCalled();
  });

  it('lets public refresh pass without a phone gate', async () => {
    const upstreamHandler = arrangeProxyPass();
    const signer = {
      sign: jest.fn().mockResolvedValue('internal-token'),
    } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const req = makeRequest('/v1/auth/refresh', { 'x-request-id': 'req-refresh' });
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(signer.sign).toHaveBeenCalledWith({ sub: 'anonymous', reqId: 'req-refresh' });
    expect(upstreamHandler).toHaveBeenCalledWith(req, res, next);
    expect(res.status).not.toHaveBeenCalled();
  });

  it.each(['/health', '/ready'])('lets %s pass to local gateway controllers', async (path) => {
    const signer = { sign: jest.fn() } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const req = makeRequest(path);
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(next).toHaveBeenCalledWith();
    expect(signer.sign).not.toHaveBeenCalled();
    expect(createProxyMiddlewareMock).not.toHaveBeenCalled();
    expect(res.status).not.toHaveBeenCalled();
  });

  it('routes /v1/auth/google to Identity without requiring Authorization', async () => {
    const upstreamHandler = arrangeProxyPass();
    const signer = {
      sign: jest.fn().mockResolvedValue('internal-token'),
    } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const req = makeRequest('/v1/auth/google', { 'x-request-id': 'req-google' });
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(signer.sign).toHaveBeenCalledWith({ sub: 'anonymous', reqId: 'req-google' });
    expect(createProxyMiddlewareMock).toHaveBeenCalledWith(
      expect.objectContaining({ target: env.IDENTITY_BASE_URL }),
    );
    expect(upstreamHandler).toHaveBeenCalledWith(req, res, next);
    expect(res.status).not.toHaveBeenCalled();
  });

  it('returns 401 for a protected mixed route without Authorization', async () => {
    const signer = { sign: jest.fn() } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const req = makeRequest('/v1/operators/profile', { 'x-request-id': 'req-mixed-auth' });
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(res.status).toHaveBeenCalledWith(401);
    expect(res.jsonBody).toMatchObject({
      success: false,
      statusCode: 401,
      error: { code: 'AUTH_TOKEN_INVALID' },
      meta: { traceId: 'req-mixed-auth' },
    });
    expect(signer.sign).not.toHaveBeenCalled();
    expect(createProxyMiddlewareMock).not.toHaveBeenCalled();
    expect(next).not.toHaveBeenCalled();
  });

  it('returns 403 AUTH_PHONE_REQUIRED for a protected mixed route when passenger hasPhone=false', async () => {
    const signer = { sign: jest.fn() } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const authorization = await makeAuthorizationHeader({
      sub: 'passenger-1',
      role: 'PASSENGER',
      hasPhone: false,
    });
    const req = makeRequest('/v1/payments/vnpay-init', {
      authorization,
      'x-request-id': 'req-mixed-phone',
    });
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(res.status).toHaveBeenCalledWith(403);
    expect(res.jsonBody).toMatchObject({
      success: false,
      statusCode: 403,
      error: { code: 'AUTH_PHONE_REQUIRED' },
      meta: { traceId: 'req-mixed-phone' },
    });
    expect(signer.sign).not.toHaveBeenCalled();
    expect(createProxyMiddlewareMock).not.toHaveBeenCalled();
    expect(next).not.toHaveBeenCalled();
  });

  it.each([
    ['POST', '/v1/operators/register'],
    ['POST', '/v1/payments/vnpay-ipn'],
    ['POST', '/v1/payments/vnpay-topup-ipn'],
  ] as const)('lets public mixed endpoint %s %s pass anonymously', async (method, path) => {
    const upstreamHandler = arrangeProxyPass();
    const signer = {
      sign: jest.fn().mockResolvedValue('internal-token'),
    } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const req = makeRequest(path, { 'x-request-id': 'req-mixed-public' }, method);
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(signer.sign).toHaveBeenCalledWith({ sub: 'anonymous', reqId: 'req-mixed-public' });
    expect(req.headers['x-internal-auth']).toBe('Bearer internal-token');
    expect(upstreamHandler).toHaveBeenCalledWith(req, res, next);
    expect(res.status).not.toHaveBeenCalled();
  });

  it('strips user Authorization before proxying while adding X-Internal-Auth with user context', async () => {
    const upstreamHandler = arrangeProxyPass();
    const signer = {
      sign: jest.fn().mockResolvedValue('internal-token'),
    } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const authorization = await makeAuthorizationHeader({
      sub: 'operator-admin-1',
      role: 'OPERATOR_ADMIN',
      operatorId: 'operator-1',
      hasPhone: true,
    });
    const req = makeRequest('/v1/operators/profile', {
      authorization,
      'x-request-id': 'req-strip-auth',
    });
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(signer.sign).toHaveBeenCalledWith({
      sub: 'operator-admin-1',
      reqId: 'req-strip-auth',
      role: 'OPERATOR_ADMIN',
      operatorId: 'operator-1',
    });
    expect(req.headers.authorization).toBeUndefined();
    expect(req.headers['x-internal-auth']).toBe('Bearer internal-token');
    expect(upstreamHandler).toHaveBeenCalledWith(req, res, next);
    expect(res.status).not.toHaveBeenCalled();
  });
});
