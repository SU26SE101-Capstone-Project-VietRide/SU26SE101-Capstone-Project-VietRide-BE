import {
  CanActivate,
  ExecutionContext,
  Inject,
  Injectable,
  UnauthorizedException,
} from '@nestjs/common';
import type { Request } from 'express';
import { jwtVerify } from 'jose';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';

const INTERNAL_AUTH_HEADER = 'x-internal-auth';

@Injectable()
export class TrackingInternalJwtGuard implements CanActivate {
  private readonly secret: Uint8Array;

  constructor(@Inject(ENV_TOKEN) env: Env) {
    this.secret = new TextEncoder().encode(env.INTERNAL_JWT_SECRET);
  }

  async canActivate(context: ExecutionContext): Promise<boolean> {
    const request = context.switchToHttp().getRequest<Request>();
    const header = request.headers[INTERNAL_AUTH_HEADER];
    const value = Array.isArray(header) ? header[0] : header;
    if (!value?.startsWith('Bearer ')) {
      throw this.unauthorized();
    }

    try {
      await jwtVerify(value.slice('Bearer '.length), this.secret, {
        algorithms: ['HS256'],
        issuer: 'vietride-gateway',
        audience: 'vietride-internal',
      });
      return true;
    } catch {
      throw this.unauthorized();
    }
  }

  private unauthorized(): UnauthorizedException {
    return new UnauthorizedException({
      errorCode: 'UNAUTHORIZED',
      detail: 'A valid Internal JWT is required.',
    });
  }
}
