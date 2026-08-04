import {
  BadRequestException,
  CanActivate,
  ExecutionContext,
  ForbiddenException,
  Inject,
  Injectable,
  UnauthorizedException,
} from '@nestjs/common';
import type { Request } from 'express';
import { TRACKING_JWT_VERIFIER } from '../app/tokens';
import type { TrackingUser } from '../auth/tracking-user.types';
import type { UserJwtVerifier } from '../auth/user-jwt.verifier';
import { TripShareOwnerParamSchema } from './trip-share-owner.dto';

export interface AuthorizedTripShareOwnerRequest extends Request {
  trackingUser: TrackingUser;
}

@Injectable()
export class TripShareOwnerJwtGuard implements CanActivate {
  constructor(
    @Inject(TRACKING_JWT_VERIFIER) private readonly jwtVerifier: UserJwtVerifier,
  ) {}

  async canActivate(context: ExecutionContext): Promise<boolean> {
    const request = context.switchToHttp().getRequest<AuthorizedTripShareOwnerRequest>();
    const token = this.readBearerToken(request.headers.authorization);
    if (!token) {
      throw new UnauthorizedException({ errorCode: 'UNAUTHORIZED', detail: 'Missing bearer token' });
    }

    let user: TrackingUser;
    try {
      user = await this.jwtVerifier.verify(token);
    } catch {
      throw new UnauthorizedException({ errorCode: 'UNAUTHORIZED', detail: 'Invalid bearer token' });
    }

    if (user.role !== 'PASSENGER') {
      throw new ForbiddenException({
        errorCode: 'ACCESS_DENIED',
        detail: 'Only passengers can manage a trip share link',
      });
    }

    const parsed = TripShareOwnerParamSchema.safeParse(request.params);
    if (!parsed.success) {
      throw new BadRequestException({
        errorCode: 'VALIDATION_FAILED',
        message: 'Invalid tripId',
        fields: parsed.error.issues.map((issue) => ({ field: 'tripId', message: issue.message })),
      });
    }

    request.trackingUser = user;
    return true;
  }

  private readBearerToken(value: string | undefined): string | undefined {
    if (!value?.startsWith('Bearer ')) return undefined;
    const token = value.slice('Bearer '.length).trim();
    return token.length > 0 ? token : undefined;
  }
}
