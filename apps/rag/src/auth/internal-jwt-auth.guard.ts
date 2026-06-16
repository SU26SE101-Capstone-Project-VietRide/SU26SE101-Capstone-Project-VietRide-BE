import { CanActivate, ExecutionContext, Inject, Injectable, UnauthorizedException } from '@nestjs/common';
import { jwtVerify } from 'jose';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import type { RequestWithRagInternalUser } from './rag-internal-user.types';

const BEARER_PREFIX = 'Bearer ';
const INTERNAL_AUTH_HEADER = 'x-internal-auth';
const INTERNAL_JWT_ISSUER = 'vietride-gateway';
const INTERNAL_JWT_AUDIENCE = 'vietride-internal';

@Injectable()
export class InternalJwtAuthGuard implements CanActivate {
  constructor(@Inject(ENV_TOKEN) private readonly env: Env) {}

  async canActivate(context: ExecutionContext): Promise<boolean> {
    const request = context.switchToHttp().getRequest<RequestWithRagInternalUser>();
    const token = this.readBearerToken(request.headers[INTERNAL_AUTH_HEADER]);
    if (!token) {
      throw new UnauthorizedException({
        errorCode: 'UNAUTHORIZED',
        detail: 'Missing internal bearer token',
      });
    }

    try {
      const result = await jwtVerify(token, new TextEncoder().encode(this.env.INTERNAL_JWT_SECRET), {
        issuer: INTERNAL_JWT_ISSUER,
        audience: INTERNAL_JWT_AUDIENCE,
        algorithms: ['HS256'],
      });
      const sub = this.readStringClaim(result.payload.sub);
      if (!sub) {
        throw new Error('Internal JWT sub claim is required');
      }
      const user = { sub };
      const role = this.readStringClaim(result.payload.role);
      const operatorId = this.readStringClaim(result.payload.operatorId);
      const reqId = this.readStringClaim(result.payload.reqId);
      request.user = {
        ...user,
        ...(role ? { role } : {}),
        ...(operatorId ? { operatorId } : {}),
        ...(reqId ? { reqId } : {}),
      };
      return true;
    } catch {
      throw new UnauthorizedException({
        errorCode: 'UNAUTHORIZED',
        detail: 'Invalid internal bearer token',
      });
    }
  }

  private readBearerToken(header: string | string[] | undefined): string | undefined {
    const value = Array.isArray(header) ? header[0] : header;
    if (!value?.startsWith(BEARER_PREFIX)) return undefined;
    const token = value.slice(BEARER_PREFIX.length).trim();
    return token.length > 0 ? token : undefined;
  }

  private readStringClaim(value: unknown): string | undefined {
    return typeof value === 'string' && value.length > 0 ? value : undefined;
  }
}
