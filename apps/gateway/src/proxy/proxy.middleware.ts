import { Logger } from '@nestjs/common';
import type { NextFunction, Request, RequestHandler as ExpressHandler, Response } from 'express';
import { createProxyMiddleware, type RequestHandler } from 'http-proxy-middleware';
import { randomUUID } from 'node:crypto';
import { InternalJwtSigner } from '../auth/internal-jwt.signer';
import { createUserJwtVerifier } from '../auth/user-jwt.verifier';
import type { Env } from '../config/env.schema';
import { buildRouteTable, matchRoute, type ProxyRoute } from '../config/routes';
import type { RequestWithUser } from '../auth/user-jwt.middleware';

type ApiErrorEnvelope = {
  success: false;
  statusCode: number;
  error: {
    code: string;
    message: string;
  };
  meta: {
    traceId: string;
    timestamp: string;
  };
};

type RequestIdSource = {
  requestId?: string;
  header?: (name: string) => string | undefined;
  headers?: Record<string, string | string[] | undefined>;
};

type GateRejection = {
  statusCode: number;
  code: 'FORBIDDEN' | 'AUTH_PHONE_REQUIRED';
  message: string;
};

function resolveRequestId(req: RequestIdSource): string {
  const headerValue =
    typeof req.header === 'function'
      ? req.header('x-request-id')
      : firstHeaderValue(req.headers?.['x-request-id']);

  return (req.requestId ?? headerValue ?? randomUUID()).toString();
}

function firstHeaderValue(value: string | string[] | undefined): string | undefined {
  return Array.isArray(value) ? value[0] : value;
}

function buildErrorEnvelope(
  statusCode: number,
  code: string,
  message: string,
  traceId: string,
): ApiErrorEnvelope {
  return {
    success: false,
    statusCode,
    error: { code, message },
    meta: { traceId, timestamp: new Date().toISOString() },
  };
}

function hasRequiredRole(route: ProxyRoute, role: string | undefined): boolean {
  return !route.requiredRoles?.length || (role !== undefined && route.requiredRoles.includes(role));
}

function hasCompletedPhoneProfile(hasPhone: unknown): boolean {
  return hasPhone === true || hasPhone === 'true';
}

function isPublicMixedSubpath(route: ProxyRoute, method: string, path: string): boolean {
  if (route.authRequired !== 'mixed') {
    return false;
  }

  const normalizedMethod = method.toUpperCase();
  return (
    route.publicSubpaths?.some(
      (subpath) =>
        subpath.path === path && (subpath.method === 'ALL' || subpath.method === normalizedMethod),
    ) ?? false
  );
}

function isPhoneGateWhitelisted(method: string, path: string): boolean {
  if (path === '/health' || path === '/ready') {
    return true;
  }

  const normalizedMethod = method.toUpperCase();
  return (
    (normalizedMethod === 'GET' && path === '/v1/users/me') ||
    (normalizedMethod === 'POST' && path === '/v1/users/me/complete-profile') ||
    (normalizedMethod === 'POST' && path === '/v1/auth/logout') ||
    (normalizedMethod === 'POST' && path === '/v1/auth/refresh')
  );
}

function validateAccessGates(
  route: ProxyRoute,
  req: Request,
  fullPath: string,
): GateRejection | undefined {
  const user = (req as RequestWithUser).user;
  const role = user?.['role'] as string | undefined;

  if (!hasRequiredRole(route, role)) {
    return {
      statusCode: 403,
      code: 'FORBIDDEN',
      message: 'Access to this resource is forbidden.',
    };
  }

  if (
    role === 'PASSENGER' &&
    !hasCompletedPhoneProfile(user?.['hasPhone']) &&
    !isPhoneGateWhitelisted(req.method, fullPath)
  ) {
    return {
      statusCode: 403,
      code: 'AUTH_PHONE_REQUIRED',
      message: 'Vui lòng hoàn tất hồ sơ trước khi tiếp tục.',
    };
  }

  return undefined;
}

/**
 * Factory returning an Express middleware (raw, not Nest) that:
 *   1. Matches incoming path against route table.
 *   2. Mints Internal JWT (HS256, 120s) with caller identity from validated User JWT (or 'anonymous').
 *   3. Forwards request via http-proxy-middleware, injecting X-Internal-Auth + X-Request-Id headers.
 *
 * Attached via `app.use()` in main.ts (after NestFactory.create) so req.url retains the full path
 * — bypassing Nest's MiddlewareConsumer which strips matched prefixes.
 *
 * Per BACKEND_SOURCE_OF_TRUTH 3.4.2 middleware chain (final step).
 */
export function createProxyHandler(env: Env, signer: InternalJwtSigner): ExpressHandler {
  const logger = new Logger('Proxy');
  const routes = buildRouteTable(env);
  const proxies = new Map<string, RequestHandler>();
  const userJwtVerifier = createUserJwtVerifier(env);

  function getProxy(route: ProxyRoute): RequestHandler {
    const key = route.target;
    let handler = proxies.get(key);
    if (!handler) {
      handler = createProxyMiddleware({
        target: route.target,
        changeOrigin: true,
        on: {
          proxyReq: (proxyReq) => {
            proxyReq.removeHeader('authorization');
          },
          error: (err, proxyReq, res) => {
            logger.error(`Upstream ${route.target} error: ${err.message}`);
            const r = res as Response;
            if (!r.headersSent) {
              const reqId = resolveRequestId(proxyReq as RequestIdSource);
              r.setHeader('X-Request-Id', reqId);
              r.status(502).json(
                buildErrorEnvelope(
                  502,
                  'UPSTREAM_UNAVAILABLE',
                  'Upstream service unavailable',
                  reqId,
                ),
              );
            }
          },
        },
      });
      proxies.set(key, handler);
    }
    return handler;
  }

  function sendUnauthorized(res: Response, reqId: string): void {
    res
      .status(401)
      .json(
        buildErrorEnvelope(
          401,
          'AUTH_TOKEN_INVALID',
          'Authorization header is required or access token is invalid.',
          reqId,
        ),
      );
  }

  return async (req: Request, res: Response, next: NextFunction): Promise<void> => {
    const fullPath = (req.originalUrl || req.url).split('?')[0] || '/';

    // Let local gateway routes through (health, ready, docs) — Nest controllers handle them.
    if (fullPath === '/health' || fullPath === '/ready' || fullPath.startsWith('/docs')) {
      return next();
    }

    // Reuse the requestId stamped by CorrelationIdMiddleware when running
    // inside the Nest pipeline; otherwise fall back to header/random because
    // this proxy is mounted as raw Express via `app.use()` before Nest router.
    const reqId = resolveRequestId(req);
    res.setHeader('X-Request-Id', reqId);

    const route = matchRoute(routes, fullPath);
    if (!route) {
      res
        .status(404)
        .json(
          buildErrorEnvelope(
            404,
            'ROUTE_NOT_FOUND',
            `No upstream registered for ${fullPath}`,
            reqId,
          ),
        );
      return;
    }

    const isAnonymousMixedEndpoint = isPublicMixedSubpath(route, req.method, fullPath);
    const requiresUserJwt = route.authRequired === 'user' || route.authRequired === 'mixed';

    let user = (req as RequestWithUser).user;
    if (requiresUserJwt && !isAnonymousMixedEndpoint && !user) {
      try {
        user = await userJwtVerifier.verifyAuthorizationHeader(req.header('authorization'));
        (req as RequestWithUser).user = user;
      } catch (err) {
        logger.warn(`JWT verify failed: ${(err as Error).message}`);
        sendUnauthorized(res, reqId);
        return;
      }
    }

    const gateRejection = validateAccessGates(route, req, fullPath);
    if (gateRejection) {
      res
        .status(gateRejection.statusCode)
        .json(
          buildErrorEnvelope(
            gateRejection.statusCode,
            gateRejection.code,
            gateRejection.message,
            reqId,
          ),
        );
      return;
    }

    const role = user?.['role'] as string | undefined;
    const operatorId = user?.['operatorId'] as string | undefined;

    const internalJwt = await signer.sign({
      sub: (user?.sub as string) ?? 'anonymous',
      reqId,
      ...(role ? { role } : {}),
      ...(operatorId ? { operatorId } : {}),
    });

    delete req.headers['authorization'];
    delete req.headers['Authorization'];
    req.headers['x-internal-auth'] = `Bearer ${internalJwt}`;
    req.headers['x-request-id'] = reqId;

    // Compute upstream path; set BOTH req.url AND req.originalUrl since http-proxy-middleware v3
    // may read either when constructing the forwarded URL.
    const search = req.originalUrl.includes('?')
      ? req.originalUrl.substring(req.originalUrl.indexOf('?'))
      : '';
    let upstreamPath: string;
    if (route.rewriteTo) {
      upstreamPath = route.rewriteTo + search;
    } else if (route.stripPrefix) {
      const trail = fullPath.substring(route.prefix.length) || '/';
      upstreamPath = trail + search;
    } else {
      upstreamPath = fullPath + search;
    }
    req.url = upstreamPath;
    (req as Request & { originalUrl: string }).originalUrl = upstreamPath;
    logger.log(`${req.method} ${fullPath} → ${route.target}${upstreamPath}`);

    getProxy(route)(req, res, next);
  };
}
