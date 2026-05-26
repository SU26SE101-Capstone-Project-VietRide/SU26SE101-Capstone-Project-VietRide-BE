import { Logger } from '@nestjs/common';
import type { NextFunction, Request, RequestHandler as ExpressHandler, Response } from 'express';
import { createProxyMiddleware, type RequestHandler } from 'http-proxy-middleware';
import { randomUUID } from 'node:crypto';
import { InternalJwtSigner } from '../auth/internal-jwt.signer';
import type { Env } from '../config/env.schema';
import { buildRouteTable, matchRoute, type ProxyRoute } from '../config/routes';
import type { RequestWithUser } from '../auth/user-jwt.middleware';

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

  function getProxy(route: ProxyRoute): RequestHandler {
    const key = route.target;
    let handler = proxies.get(key);
    if (!handler) {
      handler = createProxyMiddleware({
        target: route.target,
        changeOrigin: true,
        on: {
          error: (err, _req, res) => {
            logger.error(`Upstream ${route.target} error: ${err.message}`);
            const r = res as Response;
            if (!r.headersSent) {
              r.status(502).json({
                type: 'https://vietride.app/errors/UPSTREAM_UNAVAILABLE',
                title: 'Upstream service unavailable',
                status: 502,
                errorCode: 'UPSTREAM_UNAVAILABLE',
                detail: err.message,
              });
            }
          },
        },
      });
      proxies.set(key, handler);
    }
    return handler;
  }

  return async (req: Request, res: Response, next: NextFunction): Promise<void> => {
    const fullPath = (req.originalUrl || req.url).split('?')[0];

    // Let local gateway routes through (health, ready) — Nest controllers handle them.
    if (fullPath === '/health' || fullPath === '/ready') {
      return next();
    }

    const route = matchRoute(routes, fullPath);
    if (!route) {
      res.status(404).json({
        type: 'https://vietride.app/errors/ROUTE_NOT_FOUND',
        title: 'No upstream for path',
        status: 404,
        errorCode: 'ROUTE_NOT_FOUND',
        detail: `No upstream registered for ${fullPath}`,
      });
      return;
    }

    const reqId = (req.header('x-request-id') || randomUUID()).toString();
    res.setHeader('X-Request-Id', reqId);

    const user = (req as RequestWithUser).user;
    const internalJwt = await signer.sign({
      sub: (user?.sub as string) ?? 'anonymous',
      role: user?.['role'] as string | undefined,
      operatorId: user?.['operatorId'] as string | undefined,
      reqId,
    });

    req.headers['x-internal-auth'] = `Bearer ${internalJwt}`;
    req.headers['x-request-id'] = reqId;

    // Compute upstream path; set BOTH req.url AND req.originalUrl since http-proxy-middleware v3
    // may read either when constructing the forwarded URL.
    const search = req.originalUrl.includes('?') ? req.originalUrl.substring(req.originalUrl.indexOf('?')) : '';
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
