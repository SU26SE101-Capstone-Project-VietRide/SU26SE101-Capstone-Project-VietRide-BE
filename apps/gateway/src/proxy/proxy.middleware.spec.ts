import type { NextFunction, Request, Response } from 'express';
import { createProxyMiddleware } from 'http-proxy-middleware';
import { InternalJwtSigner } from '../auth/internal-jwt.signer';
import { envSchema } from '../config/env.schema';
import { createProxyHandler } from './proxy.middleware';

jest.mock('http-proxy-middleware', () => ({
  createProxyMiddleware: jest.fn(),
}));

const env = envSchema.parse({
  INTERNAL_JWT_SECRET: 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa',
});

const createProxyMiddlewareMock = jest.mocked(createProxyMiddleware);

function makeRequest(path: string, headers: Record<string, string> = {}): Request {
  const lowerHeaders = Object.fromEntries(
    Object.entries(headers).map(([key, value]) => [key.toLowerCase(), value]),
  );
  return {
    method: 'POST',
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
});
