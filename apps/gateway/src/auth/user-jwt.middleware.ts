import { Inject, Injectable, Logger, NestMiddleware, UnauthorizedException } from '@nestjs/common';
import type { NextFunction, Request, Response } from 'express';
import type { JWTPayload } from 'jose';
import type { Env } from '../config/env.schema';
import { ENV_TOKEN } from '../app/tokens';
import { createUserJwtVerifier, type UserJwtVerifier } from './user-jwt.verifier';

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
  private readonly verifier: UserJwtVerifier;

  constructor(@Inject(ENV_TOKEN) env: Env) {
    this.verifier = createUserJwtVerifier(env);
  }

  async use(req: Request, _res: Response, next: NextFunction): Promise<void> {
    try {
      (req as RequestWithUser).user = await this.verifier.verifyAuthorizationHeader(
        req.header('authorization'),
      );
      next();
    } catch (err) {
      this.logger.warn(`JWT verify failed: ${(err as Error).message}`);
      throw new UnauthorizedException({
        errorCode: 'AUTH_TOKEN_INVALID',
        message: 'Access token invalid or expired',
      });
    }
  }
}

export interface RequestWithUser extends Request {
  user?: JWTPayload;
}
