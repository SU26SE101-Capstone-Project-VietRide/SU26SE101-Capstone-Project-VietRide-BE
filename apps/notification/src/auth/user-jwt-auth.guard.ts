import {
  CanActivate,
  ExecutionContext,
  Inject,
  Injectable,
  UnauthorizedException,
} from '@nestjs/common';
import { NOTIFICATION_JWT_VERIFIER } from '../app/tokens';
import type { RequestWithNotificationUser } from './notification-user.types';
import type { UserJwtVerifier } from './user-jwt.verifier';

const BEARER_PREFIX = 'Bearer ';

@Injectable()
export class UserJwtAuthGuard implements CanActivate {
  constructor(@Inject(NOTIFICATION_JWT_VERIFIER) private readonly jwtVerifier: UserJwtVerifier) {}

  async canActivate(context: ExecutionContext): Promise<boolean> {
    const request = context.switchToHttp().getRequest<RequestWithNotificationUser>();
    const token = this.readBearerToken(request.headers.authorization);

    if (!token) {
      throw new UnauthorizedException({
        errorCode: 'UNAUTHORIZED',
        detail: 'Missing bearer token',
      });
    }

    try {
      request.user = await this.jwtVerifier.verify(token);
      return true;
    } catch {
      throw new UnauthorizedException({
        errorCode: 'UNAUTHORIZED',
        detail: 'Invalid bearer token',
      });
    }
  }

  private readBearerToken(authorizationHeader: string | undefined): string | undefined {
    if (!authorizationHeader?.startsWith(BEARER_PREFIX)) return undefined;
    const token = authorizationHeader.slice(BEARER_PREFIX.length).trim();
    return token.length > 0 ? token : undefined;
  }
}
