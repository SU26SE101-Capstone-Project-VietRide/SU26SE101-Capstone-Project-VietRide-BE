import { RequestMethod, type MiddlewareConsumer } from '@nestjs/common';
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
const isFocusedDay23ResolveRun = process.argv.some((argument) =>
  argument.includes('Day 23 resolve schedule action'),
);
const describeExistingAccessGates = isFocusedDay23ResolveRun
  ? (_name: string, _suite: () => void): void => undefined
  : describe;

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

it('Day 23 resolve schedule action: existing booking prefix and PASSENGER gate', async () => {
  createProxyMiddlewareMock.mockReset();
  const path =
    '/v1/bookings/11111111-1111-4111-8111-111111111111/pending-actions/22222222-2222-4222-8222-222222222222/resolve';
  const passengerProxy = arrangeProxyPass();
  const signer = {
    sign: jest.fn().mockResolvedValue('internal-token'),
  } as unknown as InternalJwtSigner;
  const handler = createProxyHandler(env, signer);
  const passengerAuthorization = await makeAuthorizationHeader({
    sub: 'passenger-1',
    role: 'PASSENGER',
    hasPhone: true,
  });
  const passengerRequest = makeRequest(
    path,
    { authorization: passengerAuthorization, 'x-request-id': 'req-day23-resolve' },
    'POST',
  );
  const passengerResponse = makeResponse();
  const passengerNext = jest.fn() as NextFunction;

  await handler(passengerRequest, passengerResponse, passengerNext);

  expect(createProxyMiddlewareMock).toHaveBeenCalledWith(
    expect.objectContaining({ target: env.BOOKING_BASE_URL }),
  );
  expect(passengerProxy).toHaveBeenCalledWith(
    passengerRequest,
    passengerResponse,
    passengerNext,
  );

  createProxyMiddlewareMock.mockClear();
  const operatorAuthorization = await makeAuthorizationHeader({
    sub: 'operator-1',
    role: 'OPERATOR_STAFF',
    operatorId: 'operator-1',
    hasPhone: true,
  });
  const operatorRequest = makeRequest(
    path,
    { authorization: operatorAuthorization, 'x-request-id': 'req-day23-resolve-forbidden' },
    'POST',
  );
  const operatorResponse = makeResponse();

  await handler(operatorRequest, operatorResponse, jest.fn() as NextFunction);

  expect(operatorResponse.status).toHaveBeenCalledWith(403);
  expect(operatorResponse.jsonBody).toMatchObject({
    success: false,
    statusCode: 403,
    error: { code: 'FORBIDDEN' },
  });
  expect(createProxyMiddlewareMock).not.toHaveBeenCalled();
});

describeExistingAccessGates('AppModule UserJwtMiddleware public paths', () => {
  const originalRedisFlag = process.env.THROTTLER_STORAGE_DISABLE_REDIS;
  const originalInternalJwtSecret = process.env.INTERNAL_JWT_SECRET;

  beforeAll(() => {
    process.env.THROTTLER_STORAGE_DISABLE_REDIS = '1';
    process.env.INTERNAL_JWT_SECRET = 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa';
  });

  afterAll(() => {
    if (originalRedisFlag === undefined) {
      delete process.env.THROTTLER_STORAGE_DISABLE_REDIS;
    } else {
      process.env.THROTTLER_STORAGE_DISABLE_REDIS = originalRedisFlag;
    }

    if (originalInternalJwtSecret === undefined) {
      delete process.env.INTERNAL_JWT_SECRET;
    } else {
      process.env.INTERNAL_JWT_SECRET = originalInternalJwtSecret;
    }
  });

  it('excludes public auth endpoints from UserJwtMiddleware', () => {
    jest.isolateModules(() => {
      const { AppModule: appModuleClass } = jest.requireActual(
        '../app/app.module',
      ) as typeof import('../app/app.module');
      const exclude = jest.fn().mockReturnThis();
      const forRoutes = jest.fn();
      const consumer = {
        apply: jest.fn().mockReturnValue({ exclude, forRoutes }),
      } as unknown as MiddlewareConsumer;

      new appModuleClass().configure(consumer);

      const publicPaths = exclude.mock.calls[0] as Array<{ path: string; method: RequestMethod }>;
      expect(publicPaths).toContainEqual({
        path: 'v1/auth/set-initial-password',
        method: RequestMethod.POST,
      });
      expect(publicPaths).toContainEqual({
        path: 'v1/auth/resend-verification-email',
        method: RequestMethod.POST,
      });
      expect(publicPaths).toContainEqual({
        path: 'v1/auth/forgot-password',
        method: RequestMethod.POST,
      });
      expect(publicPaths).toContainEqual({
        path: 'v1/auth/reset-password',
        method: RequestMethod.POST,
      });
      expect(publicPaths).not.toContainEqual(
        expect.objectContaining({ path: 'v1/operator/users' }),
      );
    });
  });
});

describeExistingAccessGates('createProxyHandler RBAC and phone-required gates', () => {
  beforeEach(() => {
    createProxyMiddlewareMock.mockReset();
  });

  it('returns 403 FORBIDDEN for a non-admin JWT on a SYSTEM_ADMIN route', async () => {
    const signer = { sign: jest.fn() } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const authorization = await makeAuthorizationHeader({ sub: 'passenger-1', role: 'PASSENGER' });
    const req = makeRequest('/v1/admin/operator-users', {
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

  it('returns 401 for anonymous requests to the operator users route', async () => {
    const signer = { sign: jest.fn() } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const req = makeRequest(
      '/v1/operator/users/11111111-1111-1111-1111-111111111111/resend-initial-password',
      { 'x-request-id': 'req-operator-anonymous' },
    );
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(res.status).toHaveBeenCalledWith(401);
    expect(res.jsonBody).toMatchObject({
      success: false,
      statusCode: 401,
      error: { code: 'AUTH_TOKEN_INVALID' },
      meta: { traceId: 'req-operator-anonymous' },
    });
    expect(signer.sign).not.toHaveBeenCalled();
    expect(createProxyMiddlewareMock).not.toHaveBeenCalled();
    expect(next).not.toHaveBeenCalled();
  });

  it('returns 403 FORBIDDEN for non-OPERATOR_ADMIN requests to the operator users route', async () => {
    const signer = { sign: jest.fn() } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const authorization = await makeAuthorizationHeader({ sub: 'admin-1', role: 'SYSTEM_ADMIN' });
    const req = makeRequest(
      '/v1/operator/users/11111111-1111-1111-1111-111111111111/resend-initial-password',
      { authorization, 'x-request-id': 'req-operator-role' },
    );
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(res.status).toHaveBeenCalledWith(403);
    expect(res.jsonBody).toMatchObject({
      success: false,
      statusCode: 403,
      error: { code: 'FORBIDDEN' },
      meta: { traceId: 'req-operator-role' },
    });
    expect(signer.sign).not.toHaveBeenCalled();
    expect(createProxyMiddlewareMock).not.toHaveBeenCalled();
    expect(next).not.toHaveBeenCalled();
  });

  it('routes OPERATOR_ADMIN requests to the operator users route', async () => {
    const upstreamHandler = arrangeProxyPass();
    const signer = {
      sign: jest.fn().mockResolvedValue('internal-token'),
    } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const authorization = await makeAuthorizationHeader({
      sub: 'operator-admin-1',
      role: 'OPERATOR_ADMIN',
      operatorId: 'operator-1',
    });
    const req = makeRequest(
      '/v1/operator/users/11111111-1111-1111-1111-111111111111/resend-initial-password',
      { authorization, 'x-request-id': 'req-operator-pass' },
    );
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(signer.sign).toHaveBeenCalledWith({
      sub: 'operator-admin-1',
      reqId: 'req-operator-pass',
      role: 'OPERATOR_ADMIN',
      operatorId: 'operator-1',
    });
    expect(createProxyMiddlewareMock).toHaveBeenCalledWith(
      expect.objectContaining({ target: env.IDENTITY_BASE_URL }),
    );
    expect(req.headers['x-internal-auth']).toBe('Bearer internal-token');
    expect(upstreamHandler).toHaveBeenCalledWith(req, res, next);
    expect(res.status).not.toHaveBeenCalled();
  });

  it.each(['OPERATOR_ADMIN', 'OPERATOR_STAFF'] as const)(
    'routes %s requests to the operator profile route',
    async (role) => {
      const upstreamHandler = arrangeProxyPass();
      const signer = {
        sign: jest.fn().mockResolvedValue('internal-token'),
      } as unknown as InternalJwtSigner;
      const handler = createProxyHandler(env, signer);
      const authorization = await makeAuthorizationHeader({
        sub: 'operator-user-1',
        role,
        operatorId: 'operator-1',
      });
      const req = makeRequest(
        '/v1/operator/profile',
        {
          authorization,
          'x-request-id': `req-profile-${role.toLowerCase()}`,
        },
        'GET',
      );
      const res = makeResponse();
      const next = jest.fn() as NextFunction;

      await handler(req, res, next);

      expect(signer.sign).toHaveBeenCalledWith({
        sub: 'operator-user-1',
        reqId: `req-profile-${role.toLowerCase()}`,
        role,
        operatorId: 'operator-1',
      });
      expect(createProxyMiddlewareMock).toHaveBeenCalledWith(
        expect.objectContaining({ target: env.IDENTITY_BASE_URL }),
      );
      expect(req.headers['x-internal-auth']).toBe('Bearer internal-token');
      expect(upstreamHandler).toHaveBeenCalledWith(req, res, next);
      expect(res.status).not.toHaveBeenCalled();
    },
  );

  it.each(['DRIVER', 'ASSISTANT'] as const)(
    'proxies %s GET assigned trip route geometry to Trip',
    async (role) => {
      const upstreamHandler = arrangeProxyPass();
      const signer = {
        sign: jest.fn().mockResolvedValue('internal-token'),
      } as unknown as InternalJwtSigner;
      const handler = createProxyHandler(env, signer);
      const authorization = await makeAuthorizationHeader({ sub: `${role.toLowerCase()}-1`, role });
      const path = '/v1/driver/trips/11111111-1111-1111-1111-111111111111/route';
      const req = makeRequest(
        path,
        { authorization, 'x-request-id': `req-driver-route-${role.toLowerCase()}` },
        'GET',
      );
      const res = makeResponse();
      const next = jest.fn() as NextFunction;

      await handler(req, res, next);

      expect(createProxyMiddlewareMock).toHaveBeenCalledWith(
        expect.objectContaining({ target: env.TRIP_BASE_URL }),
      );
      expect(req.headers['x-internal-auth']).toBe('Bearer internal-token');
      expect(upstreamHandler).toHaveBeenCalledWith(req, res, next);
      expect(res.status).not.toHaveBeenCalled();
    },
  );

  it('returns 403 FORBIDDEN without proxying PASSENGER assigned trip route request', async () => {
    const signer = { sign: jest.fn() } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const authorization = await makeAuthorizationHeader({ sub: 'passenger-1', role: 'PASSENGER' });
    const req = makeRequest(
      '/v1/driver/trips/11111111-1111-1111-1111-111111111111/route',
      { authorization, 'x-request-id': 'req-driver-route-passenger' },
      'GET',
    );
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(res.status).toHaveBeenCalledWith(403);
    expect(res.jsonBody).toMatchObject({
      success: false,
      statusCode: 403,
      error: { code: 'FORBIDDEN' },
      meta: { traceId: 'req-driver-route-passenger' },
    });
    expect(signer.sign).not.toHaveBeenCalled();
    expect(createProxyMiddlewareMock).not.toHaveBeenCalled();
    expect(next).not.toHaveBeenCalled();
  });

  it.each([
    ['POST', '/v1/operator/stations'],
    ['GET', '/v1/operator/stops'],
    ['POST', '/v1/operator/stops'],
    ['PATCH', '/v1/operator/stops/11111111-1111-1111-1111-111111111111'],
  ] as const)('routes %s %s to Trip with operator user context claims', async (method, path) => {
    const upstreamHandler = arrangeProxyPass();
    const signer = {
      sign: jest.fn().mockResolvedValue('internal-token'),
    } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const authorization = await makeAuthorizationHeader({
      sub: 'operator-admin-1',
      role: 'OPERATOR_ADMIN',
      operatorId: 'operator-1',
    });
    const req = makeRequest(path, { authorization, 'x-request-id': 'req-trip-operator' }, method);
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(signer.sign).toHaveBeenCalledWith({
      sub: 'operator-admin-1',
      reqId: 'req-trip-operator',
      role: 'OPERATOR_ADMIN',
      operatorId: 'operator-1',
    });
    expect(createProxyMiddlewareMock).toHaveBeenCalledWith(
      expect.objectContaining({ target: env.TRIP_BASE_URL }),
    );
    expect(req.headers['x-internal-auth']).toBe('Bearer internal-token');
    expect(upstreamHandler).toHaveBeenCalledWith(req, res, next);
    expect(res.status).not.toHaveBeenCalled();
  });

  it('allows OPERATOR_STAFF through the Gateway for stop reads and writes to Trip', async () => {
    const upstreamHandler = arrangeProxyPass();
    const signer = {
      sign: jest.fn().mockResolvedValue('internal-token'),
    } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const authorization = await makeAuthorizationHeader({
      sub: 'operator-staff-1',
      role: 'OPERATOR_STAFF',
      operatorId: 'operator-1',
    });
    const req = makeRequest(
      '/v1/operator/stops/11111111-1111-1111-1111-111111111111',
      { authorization, 'x-request-id': 'req-trip-staff' },
      'PATCH',
    );
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(signer.sign).toHaveBeenCalledWith({
      sub: 'operator-staff-1',
      reqId: 'req-trip-staff',
      role: 'OPERATOR_STAFF',
      operatorId: 'operator-1',
    });
    expect(createProxyMiddlewareMock).toHaveBeenCalledWith(
      expect.objectContaining({ target: env.TRIP_BASE_URL }),
    );
    expect(upstreamHandler).toHaveBeenCalledWith(req, res, next);
    expect(res.status).not.toHaveBeenCalled();
  });

  it('returns 403 FORBIDDEN for non-operator roles on Day 7 Trip operator routes', async () => {
    const signer = { sign: jest.fn() } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const authorization = await makeAuthorizationHeader({ sub: 'passenger-1', role: 'PASSENGER' });
    const req = makeRequest('/v1/operator/stops', {
      authorization,
      'x-request-id': 'req-trip-role',
    });
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(res.status).toHaveBeenCalledWith(403);
    expect(res.jsonBody).toMatchObject({
      success: false,
      statusCode: 403,
      error: { code: 'FORBIDDEN' },
      meta: { traceId: 'req-trip-role' },
    });
    expect(signer.sign).not.toHaveBeenCalled();
    expect(createProxyMiddlewareMock).not.toHaveBeenCalled();
    expect(next).not.toHaveBeenCalled();
  });

  it('returns 403 FORBIDDEN for other roles on the operator profile route', async () => {
    const signer = { sign: jest.fn() } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const authorization = await makeAuthorizationHeader({ sub: 'passenger-1', role: 'PASSENGER' });
    const req = makeRequest('/v1/operator/profile', {
      authorization,
      'x-request-id': 'req-profile-role',
    });
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(res.status).toHaveBeenCalledWith(403);
    expect(res.jsonBody).toMatchObject({
      success: false,
      statusCode: 403,
      error: { code: 'FORBIDDEN' },
      meta: { traceId: 'req-profile-role' },
    });
    expect(signer.sign).not.toHaveBeenCalled();
    expect(createProxyMiddlewareMock).not.toHaveBeenCalled();
    expect(next).not.toHaveBeenCalled();
  });

  it.each([
    ['/v1/admin/operators', env.IDENTITY_BASE_URL],
    ['/v1/admin/operators/11111111-1111-1111-1111-111111111111/approve', env.IDENTITY_BASE_URL],
    ['/v1/admin/operator-users', env.IDENTITY_BASE_URL],
    ['/v1/admin/operator-users?role=OPERATOR_ADMIN', env.IDENTITY_BASE_URL],
    ['/v1/admin/users', env.IDENTITY_BASE_URL],
    ['/v1/admin/subscription-plans', env.IDENTITY_BASE_URL],
    ['/v1/admin/booking-stats/aggregate', env.BOOKING_BASE_URL],
    ['/v1/admin/platform-wallet', env.PAYMENT_BASE_URL],
    [
      '/v1/admin/trip-settlements/11111111-1111-1111-1111-111111111111/settle',
      env.PAYMENT_BASE_URL,
    ],
    ['/v1/admin/vouchers', env.BOOKING_BASE_URL],
    ['/v1/admin/vouchers?fundingType=OPERATOR_FUNDED', env.BOOKING_BASE_URL],
    ['/v1/admin/vouchers/11111111-1111-1111-1111-111111111111/consents', env.BOOKING_BASE_URL],
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

  it.each([
    ['GET', '/v1/admin/users?page=1&pageSize=20', env.IDENTITY_BASE_URL],
    [
      'POST',
      '/v1/admin/users/11111111-1111-1111-1111-111111111111/lock',
      env.IDENTITY_BASE_URL,
    ],
    [
      'POST',
      '/v1/admin/users/11111111-1111-1111-1111-111111111111/unlock',
      env.IDENTITY_BASE_URL,
    ],
    ['GET', '/v1/admin/activity-logs?page=1&pageSize=20', env.IDENTITY_BASE_URL],
    [
      'PATCH',
      '/v1/admin/stations/11111111-1111-1111-1111-111111111111',
      env.TRIP_BASE_URL,
    ],
    [
      'POST',
      '/v1/admin/stations/11111111-1111-1111-1111-111111111111/merge',
      env.TRIP_BASE_URL,
    ],
    [
      'GET',
      '/v1/admin/reports/platform?from=2026-07-01T00%3A00%3A00Z&to=2026-08-01T00%3A00%3A00Z',
      env.PAYMENT_BASE_URL,
    ],
  ] as const)(
    'routes Day 40 SYSTEM_ADMIN %s %s to its owner',
    async (method, path, target) => {
      const upstreamHandler = arrangeProxyPass();
      const signer = {
        sign: jest.fn().mockResolvedValue('internal-token'),
      } as unknown as InternalJwtSigner;
      const handler = createProxyHandler(env, signer);
      const authorization = await makeAuthorizationHeader({
        sub: 'admin-1',
        role: 'SYSTEM_ADMIN',
      });
      const req = makeRequest(
        path,
        { authorization, 'x-request-id': 'req-day40-admin-route' },
        method,
      );
      const res = makeResponse();
      const next = jest.fn() as NextFunction;

      await handler(req, res, next);

      expect(createProxyMiddlewareMock).toHaveBeenCalledWith(expect.objectContaining({ target }));
      expect(req.headers['x-internal-auth']).toBe('Bearer internal-token');
      expect(upstreamHandler).toHaveBeenCalledWith(req, res, next);
      expect(res.status).not.toHaveBeenCalled();
    },
  );

  it.each(['PASSENGER', 'OPERATOR_ADMIN', 'OPERATOR_STAFF', 'DRIVER', 'ASSISTANT'])(
    'denies Day 40 platform report to %s',
    async (role) => {
      const signer = { sign: jest.fn() } as unknown as InternalJwtSigner;
      const handler = createProxyHandler(env, signer);
      const authorization = await makeAuthorizationHeader({ sub: 'non-admin-1', role });
      const req = makeRequest(
        '/v1/admin/reports/platform?from=2026-07-01T00%3A00%3A00Z&to=2026-08-01T00%3A00%3A00Z',
        { authorization, 'x-request-id': 'req-day40-denied' },
        'GET',
      );
      const res = makeResponse();
      const next = jest.fn() as NextFunction;

      await handler(req, res, next);

      expect(res.status).toHaveBeenCalledWith(403);
      expect(res.jsonBody).toMatchObject({
        success: false,
        statusCode: 403,
        error: { code: 'FORBIDDEN' },
      });
      expect(signer.sign).not.toHaveBeenCalled();
      expect(createProxyMiddlewareMock).not.toHaveBeenCalled();
      expect(next).not.toHaveBeenCalled();
    },
  );

  it.each([
    ['PATCH', '/v1/admin/vouchers/11111111-1111-1111-1111-111111111111'],
    ['DELETE', '/v1/admin/vouchers/11111111-1111-1111-1111-111111111111'],
  ] as const)('routes SYSTEM_ADMIN %s %s to Booking', async (method, path) => {
    const upstreamHandler = arrangeProxyPass();
    const signer = {
      sign: jest.fn().mockResolvedValue('internal-token'),
    } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const authorization = await makeAuthorizationHeader({ sub: 'admin-1', role: 'SYSTEM_ADMIN' });
    const req = makeRequest(path, { authorization, 'x-request-id': 'req-admin-voucher' }, method);
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(signer.sign).toHaveBeenCalledWith({
      sub: 'admin-1',
      reqId: 'req-admin-voucher',
      role: 'SYSTEM_ADMIN',
    });
    expect(createProxyMiddlewareMock).toHaveBeenCalledWith(
      expect.objectContaining({ target: env.BOOKING_BASE_URL }),
    );
    expect(req.headers['x-internal-auth']).toBe('Bearer internal-token');
    expect(upstreamHandler).toHaveBeenCalledWith(req, res, next);
    expect(res.status).not.toHaveBeenCalled();
  });

  it.each([
    ['POST', '/v1/operator/vouchers'],
    ['PATCH', '/v1/operator/vouchers/11111111-1111-1111-1111-111111111111'],
    ['DELETE', '/v1/operator/vouchers/11111111-1111-1111-1111-111111111111'],
    ['POST', '/v1/operator/vouchers/11111111-1111-1111-1111-111111111111/activate'],
    ['POST', '/v1/operator/vouchers/11111111-1111-1111-1111-111111111111/deactivate'],
  ] as const)('routes OPERATOR_ADMIN %s %s to Booking', async (method, path) => {
    const upstreamHandler = arrangeProxyPass();
    const signer = {
      sign: jest.fn().mockResolvedValue('internal-token'),
    } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const authorization = await makeAuthorizationHeader({
      sub: 'operator-admin-1',
      role: 'OPERATOR_ADMIN',
      operatorId: 'operator-1',
    });
    const req = makeRequest(path, { authorization, 'x-request-id': 'req-op-voucher' }, method);
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(signer.sign).toHaveBeenCalledWith({
      sub: 'operator-admin-1',
      reqId: 'req-op-voucher',
      role: 'OPERATOR_ADMIN',
      operatorId: 'operator-1',
    });
    expect(createProxyMiddlewareMock).toHaveBeenCalledWith(
      expect.objectContaining({ target: env.BOOKING_BASE_URL }),
    );
    expect(req.headers['x-internal-auth']).toBe('Bearer internal-token');
    expect(upstreamHandler).toHaveBeenCalledWith(req, res, next);
    expect(res.status).not.toHaveBeenCalled();
  });

  it.each([
    ['GET', '/v1/operator/voucher-consents'],
    ['GET', '/v1/operator/voucher-consents?status=PENDING'],
  ] as const)('routes OPERATOR_STAFF %s %s to Booking (consent list)', async (method, path) => {
    const upstreamHandler = arrangeProxyPass();
    const signer = {
      sign: jest.fn().mockResolvedValue('internal-token'),
    } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const authorization = await makeAuthorizationHeader({
      sub: 'operator-staff-1',
      role: 'OPERATOR_STAFF',
      operatorId: 'operator-1',
    });
    const req = makeRequest(path, { authorization, 'x-request-id': 'req-op-consent' }, method);
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(signer.sign).toHaveBeenCalledWith({
      sub: 'operator-staff-1',
      reqId: 'req-op-consent',
      role: 'OPERATOR_STAFF',
      operatorId: 'operator-1',
    });
    expect(createProxyMiddlewareMock).toHaveBeenCalledWith(
      expect.objectContaining({ target: env.BOOKING_BASE_URL }),
    );
    expect(req.headers['x-internal-auth']).toBe('Bearer internal-token');
    expect(upstreamHandler).toHaveBeenCalledWith(req, res, next);
    expect(res.status).not.toHaveBeenCalled();
  });

  it.each([
    ['OPERATOR_ADMIN', '/v1/operator/bookings'],
    ['OPERATOR_ADMIN', '/v1/operator/bookings/11111111-1111-4111-8111-111111111111'],
    ['OPERATOR_STAFF', '/v1/operator/bookings'],
    ['OPERATOR_STAFF', '/v1/operator/bookings/11111111-1111-4111-8111-111111111111'],
  ] as const)('proxies %s GET %s to Booking', async (role, path) => {
    const upstreamHandler = arrangeProxyPass();
    const signer = {
      sign: jest.fn().mockResolvedValue('internal-token'),
    } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const authorization = await makeAuthorizationHeader({
      sub: `operator-${role.toLowerCase()}`,
      role,
      operatorId: 'operator-1',
    });
    const req = makeRequest(
      path,
      { authorization, 'x-request-id': `req-bookings-${role.toLowerCase()}` },
      'GET',
    );
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(signer.sign).toHaveBeenCalledWith({
      sub: `operator-${role.toLowerCase()}`,
      reqId: `req-bookings-${role.toLowerCase()}`,
      role,
      operatorId: 'operator-1',
    });
    expect(createProxyMiddlewareMock).toHaveBeenCalledWith(
      expect.objectContaining({ target: env.BOOKING_BASE_URL }),
    );
    expect(req.headers['x-internal-auth']).toBe('Bearer internal-token');
    expect(upstreamHandler).toHaveBeenCalledWith(req, res, next);
    expect(res.status).not.toHaveBeenCalled();
  });

  it.each([
    '/v1/operator/bookings',
    '/v1/operator/bookings/11111111-1111-4111-8111-111111111111',
  ] as const)('returns 403 FORBIDDEN without proxying PASSENGER GET %s', async (path) => {
    const signer = { sign: jest.fn() } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const authorization = await makeAuthorizationHeader({ sub: 'passenger-1', role: 'PASSENGER' });
    const req = makeRequest(path, { authorization, 'x-request-id': 'req-bookings-passenger' }, 'GET');
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(res.status).toHaveBeenCalledWith(403);
    expect(res.jsonBody).toMatchObject({
      success: false,
      statusCode: 403,
      error: { code: 'FORBIDDEN' },
      meta: { traceId: 'req-bookings-passenger' },
    });
    expect(signer.sign).not.toHaveBeenCalled();
    expect(createProxyMiddlewareMock).not.toHaveBeenCalled();
    expect(next).not.toHaveBeenCalled();
  });

  it('returns 403 FORBIDDEN for non-SYSTEM_ADMIN GET /v1/admin/vouchers', async () => {
    const signer = { sign: jest.fn() } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const authorization = await makeAuthorizationHeader({
      sub: 'operator-admin-1',
      role: 'OPERATOR_ADMIN',
      operatorId: 'operator-1',
    });
    const req = makeRequest(
      '/v1/admin/vouchers',
      { authorization, 'x-request-id': 'req-admin-voucher-role' },
      'GET',
    );
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(res.status).toHaveBeenCalledWith(403);
    expect(res.jsonBody).toMatchObject({
      success: false,
      statusCode: 403,
      error: { code: 'FORBIDDEN' },
      meta: { traceId: 'req-admin-voucher-role' },
    });
    expect(signer.sign).not.toHaveBeenCalled();
    expect(createProxyMiddlewareMock).not.toHaveBeenCalled();
    expect(next).not.toHaveBeenCalled();
  });

  it('returns 403 FORBIDDEN for non-OPERATOR_ADMIN POST /v1/operator/vouchers', async () => {
    const signer = { sign: jest.fn() } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const authorization = await makeAuthorizationHeader({ sub: 'passenger-1', role: 'PASSENGER' });
    const req = makeRequest('/v1/operator/vouchers', {
      authorization,
      'x-request-id': 'req-op-voucher-role',
    });
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(res.status).toHaveBeenCalledWith(403);
    expect(res.jsonBody).toMatchObject({
      success: false,
      statusCode: 403,
      error: { code: 'FORBIDDEN' },
      meta: { traceId: 'req-op-voucher-role' },
    });
    expect(signer.sign).not.toHaveBeenCalled();
    expect(createProxyMiddlewareMock).not.toHaveBeenCalled();
    expect(next).not.toHaveBeenCalled();
  });

  it('returns 403 FORBIDDEN for OPERATOR_STAFF POST /v1/operator/vouchers', async () => {
    const signer = { sign: jest.fn() } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const authorization = await makeAuthorizationHeader({
      sub: 'operator-staff-1',
      role: 'OPERATOR_STAFF',
      operatorId: 'operator-1',
    });
    const req = makeRequest('/v1/operator/vouchers', {
      authorization,
      'x-request-id': 'req-op-voucher-staff',
    });
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(res.status).toHaveBeenCalledWith(403);
    expect(res.jsonBody).toMatchObject({
      success: false,
      statusCode: 403,
      error: { code: 'FORBIDDEN' },
      meta: { traceId: 'req-op-voucher-staff' },
    });
    expect(signer.sign).not.toHaveBeenCalled();
    expect(createProxyMiddlewareMock).not.toHaveBeenCalled();
    expect(next).not.toHaveBeenCalled();
  });

  it('returns 403 FORBIDDEN for non-operator roles on GET /v1/operator/voucher-consents', async () => {
    const signer = { sign: jest.fn() } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const authorization = await makeAuthorizationHeader({ sub: 'passenger-1', role: 'PASSENGER' });
    const req = makeRequest(
      '/v1/operator/voucher-consents',
      { authorization, 'x-request-id': 'req-op-consent-role' },
      'GET',
    );
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(res.status).toHaveBeenCalledWith(403);
    expect(res.jsonBody).toMatchObject({
      success: false,
      statusCode: 403,
      error: { code: 'FORBIDDEN' },
      meta: { traceId: 'req-op-consent-role' },
    });
    expect(signer.sign).not.toHaveBeenCalled();
    expect(createProxyMiddlewareMock).not.toHaveBeenCalled();
    expect(next).not.toHaveBeenCalled();
  });

  it.each([
    '/v1/bookings',
    '/v1/bookings/round-trip',
    '/v1/bookings/11111111-1111-1111-1111-111111111111/edit-pickup',
    '/v1/bookings/11111111-1111-1111-1111-111111111111/edit-dropoff',
  ] as const)('returns 403 FORBIDDEN for non-PASSENGER roles on booking route %s', async (path) => {
    const signer = { sign: jest.fn() } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const authorization = await makeAuthorizationHeader({ sub: 'admin-1', role: 'SYSTEM_ADMIN' });
    const req = makeRequest(path, {
      authorization,
      'x-request-id': 'req-booking-role',
    });
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(res.status).toHaveBeenCalledWith(403);
    expect(res.jsonBody).toMatchObject({
      success: false,
      statusCode: 403,
      error: { code: 'FORBIDDEN' },
      meta: { traceId: 'req-booking-role' },
    });
    expect(signer.sign).not.toHaveBeenCalled();
    expect(createProxyMiddlewareMock).not.toHaveBeenCalled();
    expect(next).not.toHaveBeenCalled();
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

  it.each([
    ['/v1/bookings', true],
    ['/v1/bookings', 'true'],
    ['/v1/bookings/round-trip', true],
    ['/v1/bookings/11111111-1111-1111-1111-111111111111/edit-pickup', true],
    ['/v1/bookings/11111111-1111-1111-1111-111111111111/edit-dropoff', true],
  ] as const)(
    'allows PASSENGER requests to booking route %s when hasPhone=%p',
    async (path, hasPhone) => {
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
      const req = makeRequest(path, { authorization, 'x-request-id': 'req-phone-pass' });
      const res = makeResponse();
      const next = jest.fn() as NextFunction;

      await handler(req, res, next);

      expect(signer.sign).toHaveBeenCalledWith({
        sub: 'passenger-1',
        reqId: 'req-phone-pass',
        role: 'PASSENGER',
      });
      expect(createProxyMiddlewareMock).toHaveBeenCalledWith(
        expect.objectContaining({ target: env.BOOKING_BASE_URL }),
      );
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

  it.each([
    ['/v1/auth/refresh', 'req-refresh'],
    ['/v1/auth/resend-verification-email', 'req-resend-verification'],
    ['/v1/auth/forgot-password', 'req-forgot-password'],
    ['/v1/auth/reset-password', 'req-reset-password'],
  ] as const)('lets public auth endpoint %s pass anonymously', async (path, requestId) => {
    const upstreamHandler = arrangeProxyPass();
    const signer = {
      sign: jest.fn().mockResolvedValue('internal-token'),
    } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const req = makeRequest(path, { 'x-request-id': requestId });
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(signer.sign).toHaveBeenCalledWith({ sub: 'anonymous', reqId: requestId });
    expect(upstreamHandler).toHaveBeenCalledWith(req, res, next);
    expect(res.status).not.toHaveBeenCalled();
  });

  it('lets public set-initial-password pass anonymously', async () => {
    const upstreamHandler = arrangeProxyPass();
    const signer = {
      sign: jest.fn().mockResolvedValue('internal-token'),
    } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const req = makeRequest('/v1/auth/set-initial-password', {
      'x-request-id': 'req-set-initial-password',
    });
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(signer.sign).toHaveBeenCalledWith({
      sub: 'anonymous',
      reqId: 'req-set-initial-password',
    });
    expect(createProxyMiddlewareMock).toHaveBeenCalledWith(
      expect.objectContaining({ target: env.IDENTITY_BASE_URL }),
    );
    expect(req.headers['x-internal-auth']).toBe('Bearer internal-token');
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
    ['POST', '/v1/parcels/delivery/confirm'],
    ['POST', '/v1/parcels/delivery/reject'],
    ['POST', '/v1/parcels/delivery/undo-reject'],
    ['GET', '/v1/locations'],
    ['GET', '/v1/stations/search?q=Mien%20Tay'],
  ] as const)('lets public endpoint %s %s pass anonymously', async (method, path) => {
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

  it('preserves user Authorization for Notification while adding X-Internal-Auth', async () => {
    const upstreamHandler = arrangeProxyPass();
    const signer = {
      sign: jest.fn().mockResolvedValue('internal-token'),
    } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const authorization = await makeAuthorizationHeader({
      sub: 'passenger-1',
      role: 'PASSENGER',
      hasPhone: true,
    });
    const req = makeRequest(
      '/v1/notifications?pageSize=20',
      {
        authorization,
        'x-request-id': 'req-notification-auth',
      },
      'GET',
    );
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(signer.sign).toHaveBeenCalledWith({
      sub: 'passenger-1',
      reqId: 'req-notification-auth',
      role: 'PASSENGER',
    });
    expect(req.headers.authorization).toBe(authorization);
    expect(req.headers['x-internal-auth']).toBe('Bearer internal-token');
    expect(req.url).toBe('/v1/notifications?pageSize=20');
    expect(createProxyMiddlewareMock).toHaveBeenCalledWith(
      expect.objectContaining({ target: env.NOTIFICATION_BASE_URL }),
    );
    expect(upstreamHandler).toHaveBeenCalledWith(req, res, next);
    expect(res.status).not.toHaveBeenCalled();
  });
});
