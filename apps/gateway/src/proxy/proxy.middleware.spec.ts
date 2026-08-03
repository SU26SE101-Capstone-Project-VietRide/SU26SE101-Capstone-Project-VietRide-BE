import type { NextFunction, Request, Response } from 'express';
import { createProxyMiddleware } from 'http-proxy-middleware';
import { EventEmitter } from 'node:events';
import { InternalJwtSigner } from '../auth/internal-jwt.signer';
import type { RequestWithUser } from '../auth/user-jwt.middleware';
import { envSchema } from '../config/env.schema';
import { createProxyHandler } from './proxy.middleware';

jest.mock('http-proxy-middleware', () => ({
  createProxyMiddleware: jest.fn(),
}));

const env = envSchema.parse({
  INTERNAL_JWT_SECRET: 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa',
});

const createProxyMiddlewareMock = jest.mocked(createProxyMiddleware);

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

function makeResponse(): Response & { statusCodeValue?: number; jsonBody?: unknown } {
  const res = {
    setHeader: jest.fn(),
    status: jest.fn(function status(this: Response & { statusCodeValue?: number }, code: number) {
      this.statusCodeValue = code;
      return this;
    }),
    json: jest.fn(function json(this: Response & { jsonBody?: unknown }, body: unknown) {
      this.jsonBody = body;
      return this;
    }),
  } as unknown as Response & { statusCodeValue?: number; jsonBody?: unknown };

  return res;
}

describe('createProxyHandler auth enforcement', () => {
  beforeEach(() => {
    createProxyMiddlewareMock.mockReset();
  });

  it('returns an ADR 0004 envelope for unknown routes', async () => {
    const signer = { sign: jest.fn() } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const req = makeRequest('/v1/unknown', { 'x-request-id': 'req-missing-route' });
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(res.setHeader).toHaveBeenCalledWith('X-Request-Id', 'req-missing-route');
    expect(res.status).toHaveBeenCalledWith(404);
    expect(res.jsonBody).toMatchObject({
      success: false,
      statusCode: 404,
      error: {
        code: 'ROUTE_NOT_FOUND',
        message: 'No upstream registered for /v1/unknown',
      },
      meta: { traceId: 'req-missing-route' },
    });
    expect(signer.sign).not.toHaveBeenCalled();
    expect(createProxyMiddlewareMock).not.toHaveBeenCalled();
    expect(next).not.toHaveBeenCalled();
  });

  it.each(['/.well-known/assetlinks.json', '/auth/set-password', '/payments/return'])(
    'lets %s fall through to Nest controllers',
    async (path) => {
      const signer = { sign: jest.fn() } as unknown as InternalJwtSigner;
      const handler = createProxyHandler(env, signer);
      const req = makeRequest(path, {}, 'GET');
      const res = makeResponse();
      const next = jest.fn() as NextFunction;

      await handler(req, res, next);

      expect(next).toHaveBeenCalled();
      expect(res.status).not.toHaveBeenCalled();
      expect(signer.sign).not.toHaveBeenCalled();
      expect(createProxyMiddlewareMock).not.toHaveBeenCalled();
    },
  );

  it('returns 401 for POST /v1/auth/logout without Authorization', async () => {
    const signer = { sign: jest.fn() } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const req = makeRequest('/v1/auth/logout');
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(res.status).toHaveBeenCalledWith(401);
    expect(res.jsonBody).toMatchObject({
      success: false,
      statusCode: 401,
      error: { code: 'AUTH_TOKEN_INVALID' },
    });
    expect(signer.sign).not.toHaveBeenCalled();
    expect(createProxyMiddlewareMock).not.toHaveBeenCalled();
    expect(next).not.toHaveBeenCalled();
  });

  it('keeps public auth endpoints proxyable without Authorization', async () => {
    const upstreamHandler = jest.fn();
    createProxyMiddlewareMock.mockReturnValue(
      upstreamHandler as unknown as ReturnType<typeof createProxyMiddleware>,
    );
    const signer = {
      sign: jest.fn().mockResolvedValue('internal-token'),
    } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const req = makeRequest('/v1/auth/login', { 'x-request-id': 'req-public' });
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(signer.sign).toHaveBeenCalledWith({ sub: 'anonymous', reqId: 'req-public' });
    expect(req.headers['x-internal-auth']).toBe('Bearer internal-token');
    expect(createProxyMiddlewareMock).toHaveBeenCalledWith(
      expect.objectContaining({ target: env.IDENTITY_BASE_URL }),
    );
    expect(upstreamHandler).toHaveBeenCalledWith(req, res, next);
    expect(res.status).not.toHaveBeenCalled();
  });

  it('proxies anonymous shared-trip context with query and share-token headers intact', async () => {
    const upstreamHandler = jest.fn();
    createProxyMiddlewareMock.mockReturnValue(
      upstreamHandler as unknown as ReturnType<typeof createProxyMiddleware>,
    );
    const signer = {
      sign: jest.fn().mockResolvedValue('internal-token'),
    } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const req = makeRequest(
      '/v1/tracking/shared-trip/context?locale=vi',
      {
        'x-request-id': 'req-shared-context',
        'x-trip-share-token': 'v1.grant.signature',
      },
      'GET',
    );
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(signer.sign).toHaveBeenCalledWith({ sub: 'anonymous', reqId: 'req-shared-context' });
    expect(req.headers['x-trip-share-token']).toBe('v1.grant.signature');
    expect(req.headers['x-internal-auth']).toBe('Bearer internal-token');
    expect(req.url).toBe('/v1/tracking/shared-trip/context?locale=vi');
    expect(createProxyMiddlewareMock).toHaveBeenCalledWith(
      expect.objectContaining({ target: env.TRACKING_BASE_URL }),
    );
    expect(upstreamHandler).toHaveBeenCalledWith(req, res, next);
    expect(res.status).not.toHaveBeenCalled();
  });

  it('proxies Day 9 Trip route families instead of returning ROUTE_NOT_FOUND', async () => {
    const upstreamHandler = jest.fn();
    createProxyMiddlewareMock.mockReturnValue(
      upstreamHandler as unknown as ReturnType<typeof createProxyMiddleware>,
    );
    const signer = {
      sign: jest.fn().mockResolvedValue('internal-token'),
    } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const cases = [
      ['GET', '/v1/vehicle-types'],
      ['POST', '/v1/operator/vehicles'],
      ['POST', '/v1/operator/driver-schedules'],
    ] as const;

    for (const [method, path] of cases) {
      const req = makeRequest(path, { 'x-request-id': `req-${path.split('/').pop()}` }, method);
      (req as RequestWithUser).user = {
        sub: 'operator-user-id',
        role: 'OPERATOR_ADMIN',
        operatorId: 'operator-id',
      };
      const res = makeResponse();
      const next = jest.fn() as NextFunction;

      await handler(req, res, next);

      expect(res.status).not.toHaveBeenCalledWith(404);
      expect(req.headers['x-internal-auth']).toBe('Bearer internal-token');
      expect(req.url).toBe(path);
      expect(upstreamHandler).toHaveBeenCalledWith(req, res, next);
    }

    expect(createProxyMiddlewareMock).toHaveBeenCalledWith(
      expect.objectContaining({ target: env.TRIP_BASE_URL }),
    );
  });

  it('returns an ADR 0004 envelope when the upstream proxy fails', async () => {
    createProxyMiddlewareMock.mockImplementation((options) => {
      return ((req, res) => {
        options.on?.error?.(new Error('connect ECONNREFUSED'), req, res);
      }) as ReturnType<typeof createProxyMiddleware>;
    });
    const signer = {
      sign: jest.fn().mockResolvedValue('internal-token'),
    } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const req = makeRequest('/v1/auth/login', { 'x-request-id': 'req-upstream' });
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(res.setHeader).toHaveBeenCalledWith('X-Request-Id', 'req-upstream');
    expect(res.status).toHaveBeenCalledWith(502);
    expect(res.jsonBody).toMatchObject({
      success: false,
      statusCode: 502,
      error: {
        code: 'UPSTREAM_UNAVAILABLE',
        message: 'Upstream service unavailable',
      },
      meta: { traceId: 'req-upstream' },
    });
    expect(next).not.toHaveBeenCalled();
  });

  it('destroys the upstream request when the downstream client disconnects', async () => {
    const proxyReq = {
      destroyed: false,
      destroy: jest.fn(),
      removeHeader: jest.fn(),
    };
    createProxyMiddlewareMock.mockImplementation((options) => {
      return ((req, res) => {
        options.on?.proxyReq?.(proxyReq as never, req, res, {});
      }) as ReturnType<typeof createProxyMiddleware>;
    });
    const signer = {
      sign: jest.fn().mockResolvedValue('internal-token'),
    } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const req = Object.assign(
      new EventEmitter(),
      makeRequest('/v1/auth/login', { 'x-request-id': 'req-client-abort' }),
    ) as unknown as Request;
    const res = Object.assign(new EventEmitter(), makeResponse(), {
      writableEnded: false,
      destroyed: false,
    }) as unknown as Response & EventEmitter;

    await handler(req, res, jest.fn() as NextFunction);
    res.emit('close');

    expect(proxyReq.destroy).toHaveBeenCalledTimes(1);
  });

  it('does not destroy the upstream request after a normal response finishes', async () => {
    const proxyReq = {
      destroyed: false,
      destroy: jest.fn(),
      removeHeader: jest.fn(),
    };
    createProxyMiddlewareMock.mockImplementation((options) => {
      return ((req, res) => {
        options.on?.proxyReq?.(proxyReq as never, req, res, {});
      }) as ReturnType<typeof createProxyMiddleware>;
    });
    const signer = {
      sign: jest.fn().mockResolvedValue('internal-token'),
    } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const req = Object.assign(
      new EventEmitter(),
      makeRequest('/v1/auth/login', { 'x-request-id': 'req-complete' }),
    ) as unknown as Request;
    const res = Object.assign(new EventEmitter(), makeResponse(), {
      writableEnded: false,
      destroyed: false,
    }) as unknown as Response & EventEmitter;

    await handler(req, res, jest.fn() as NextFunction);
    Object.defineProperty(res, 'writableEnded', { value: true });
    res.emit('finish');
    res.emit('close');

    expect(proxyReq.destroy).not.toHaveBeenCalled();
  });

  it.each([
    ['/v1/admin/dashboard/summary', 'OPERATOR_ADMIN'],
    ['/v1/operator/parcel-stats', 'OPERATOR_STAFF'],
    ['/v1/admin/revenue/analytics', 'OPERATOR_ADMIN'],
    ['/v1/operator/revenue/analytics', 'OPERATOR_STAFF'],
    ['/v1/operator/trips', 'OPERATOR_STAFF'],
    [
      '/v1/operator/parcel-route-fares/11111111-1111-4111-8111-111111111111/batch',
      'OPERATOR_STAFF',
    ],
  ])('rejects UI-gap route %s for role %s at Gateway', async (path, role) => {
    const signer = { sign: jest.fn() } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const req = makeRequest(path, { 'x-request-id': 'req-ui23-forbidden' }, 'GET');
    (req as RequestWithUser).user = {
      sub: 'ui23-user-id',
      role,
      operatorId: '11111111-1111-4111-8111-111111111111',
    };
    const res = makeResponse();
    const next = jest.fn() as NextFunction;

    await handler(req, res, next);

    expect(res.status).toHaveBeenCalledWith(403);
    expect(res.jsonBody).toMatchObject({
      success: false,
      statusCode: 403,
      error: { code: 'FORBIDDEN' },
      meta: { traceId: 'req-ui23-forbidden' },
    });
    expect(signer.sign).not.toHaveBeenCalled();
    expect(createProxyMiddlewareMock).not.toHaveBeenCalled();
    expect(next).not.toHaveBeenCalled();
  });

  it('proxies every UI-gap public facade for its exact allowed role', async () => {
    const upstreamHandler = jest.fn();
    createProxyMiddlewareMock.mockReturnValue(
      upstreamHandler as unknown as ReturnType<typeof createProxyMiddleware>,
    );
    const signer = {
      sign: jest.fn().mockResolvedValue('internal-token'),
    } as unknown as InternalJwtSigner;
    const handler = createProxyHandler(env, signer);
    const cases = [
      ['GET', '/v1/admin/dashboard/summary', 'SYSTEM_ADMIN'],
      ['GET', '/v1/operator/parcel-stats', 'OPERATOR_ADMIN'],
      ['GET', '/v1/admin/revenue/analytics', 'SYSTEM_ADMIN'],
      ['GET', '/v1/operator/revenue/analytics', 'OPERATOR_ADMIN'],
      ['GET', '/v1/operator/trips', 'OPERATOR_ADMIN'],
      [
        'PUT',
        '/v1/operator/parcel-route-fares/11111111-1111-4111-8111-111111111111/batch',
        'OPERATOR_ADMIN',
      ],
    ] as const;

    for (const [method, path, role] of cases) {
      const req = makeRequest(path, { 'x-request-id': `req-${role}-${method}` }, method);
      (req as RequestWithUser).user = {
        sub: 'ui23-user-id',
        role,
        operatorId: '11111111-1111-4111-8111-111111111111',
      };
      const res = makeResponse();
      const next = jest.fn() as NextFunction;

      await handler(req, res, next);

      expect(res.status).not.toHaveBeenCalled();
      expect(req.headers['x-internal-auth']).toBe('Bearer internal-token');
      expect(req.url).toBe(path);
      expect(upstreamHandler).toHaveBeenCalledWith(req, res, next);
    }
  });
});
