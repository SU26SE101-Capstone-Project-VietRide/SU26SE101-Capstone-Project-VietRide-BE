import { Inject, Injectable, Logger, NestMiddleware, UnauthorizedException } from '@nestjs/common';
import type { NextFunction, Request, Response } from 'express';
import { createRemoteJWKSet, jwtVerify, type JWTPayload } from 'jose';
import type { Env } from '../config/env.schema';
import { ENV_TOKEN } from '../app/tokens';

/**
 * Verifies User Access Token (RS256) via JWKS from Identity Service.
 * Per BACKEND_SOURCE_OF_TRUTH 6 + 3.4.2.
 *
 * Day 2 scope: skeleton with lazy JWKS init. Day 3+ adds Redis cache for JWKS,
 * key rotation, leeway tuning. If Identity not yet up (Day 2 boot), endpoints
 * requiring auth will return 503 — expected pre-Sprint 3.
 */
@Injectable()
export class UserJwtMiddleware implements NestMiddleware {
  private readonly logger = new Logger('UserJwtMiddleware');
  private readonly jwks: ReturnType<typeof createRemoteJWKSet>;

  constructor(@Inject(ENV_TOKEN) private readonly env: Env) {
    this.jwks = createRemoteJWKSet(new URL(env.JWT_PUBLIC_KEY_URL));
  }

  async use(req: Request, _res: Response, next: NextFunction): Promise<void> {
    const auth = req.header('authorization');
    if (!auth?.toLowerCase().startsWith('bearer ')) {
      throw new UnauthorizedException({ errorCode: 'MISSING_BEARER', message: 'Authorization header required' });
    }

    const token = auth.slice(7).trim();
    try {
      const { payload } = await jwtVerify(token, this.jwks, {
        issuer: this.env.JWT_ISSUER,
        audience: this.env.JWT_AUDIENCE,
        clockTolerance: 5,
      });
      (req as RequestWithUser).user = payload;
      next();
    } catch (err) {
      this.logger.warn(`JWT verify failed: ${(err as Error).message}`);
      throw new UnauthorizedException({ errorCode: 'INVALID_TOKEN', message: 'Access token invalid or expired' });
    }
  }
}

export interface RequestWithUser extends Request {
  user?: JWTPayload;
}
