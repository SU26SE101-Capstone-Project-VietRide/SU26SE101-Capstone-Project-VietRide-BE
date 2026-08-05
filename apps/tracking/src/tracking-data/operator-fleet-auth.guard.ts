import { CanActivate, ExecutionContext, ForbiddenException, Inject, Injectable, UnauthorizedException } from '@nestjs/common';
import type { Request } from 'express';
import { TRACKING_JWT_VERIFIER } from '../app/tokens';
import type { TrackingUser } from '../auth/tracking-user.types';
import type { UserJwtVerifier } from '../auth/user-jwt.verifier';

@Injectable()
export class OperatorFleetAuthGuard implements CanActivate {
  constructor(@Inject(TRACKING_JWT_VERIFIER) private readonly verifier: UserJwtVerifier) {}

  async canActivate(context: ExecutionContext): Promise<boolean> {
    const request = context.switchToHttp().getRequest<Request>();
    const header = request.headers.authorization;
    if (!header?.startsWith('Bearer ')) throw new UnauthorizedException({ errorCode: 'UNAUTHORIZED' });
    let user: TrackingUser;
    try {
      user = await this.verifier.verify(header.slice(7).trim());
    } catch {
      throw new UnauthorizedException({ errorCode: 'UNAUTHORIZED' });
    }
    if (!['OPERATOR_ADMIN', 'OPERATOR_STAFF'].includes(user.role) || !user.operatorId) {
      throw new ForbiddenException({ errorCode: 'FORBIDDEN', detail: 'Operator scope is required' });
    }
    (request as unknown as { user: TrackingUser }).user = user;
    return true;
  }
}
