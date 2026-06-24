import { BadRequestException, CanActivate, ExecutionContext, ForbiddenException, Inject, Injectable, NotFoundException, ServiceUnavailableException, UnauthorizedException } from '@nestjs/common';
import { Request } from 'express';
import { TRACKING_AUTHORIZATION_ADAPTER, TRACKING_JWT_VERIFIER } from '../app/tokens';
import type { TrackingUser } from '../auth/tracking-user.types';
import type { UserJwtVerifier } from '../auth/user-jwt.verifier';
import type { TrackingAuthorizationAdapter } from '../authorization/tracking-authorization.adapter';
import { TripIdParamSchema } from './dto/tracking-data-query.dto';

@Injectable()
export class TrackingDataAuthGuard implements CanActivate {
  constructor(
    @Inject(TRACKING_JWT_VERIFIER) private readonly jwtVerifier: UserJwtVerifier,
    @Inject(TRACKING_AUTHORIZATION_ADAPTER)
    private readonly authorizationAdapter: TrackingAuthorizationAdapter,
  ) {}

  async canActivate(context: ExecutionContext): Promise<boolean> {
    const request = context.switchToHttp().getRequest<Request>();
    const token = this.readBearerToken(request.headers.authorization);
    if (!token) {
      throw new UnauthorizedException({
        errorCode: 'UNAUTHORIZED',
        detail: 'Missing bearer token',
      });
    }

    let user: TrackingUser;
    try {
      user = await this.jwtVerifier.verify(token);
    } catch {
      throw new UnauthorizedException({
        errorCode: 'UNAUTHORIZED',
        detail: 'Invalid bearer token',
      });
    }

    const rawTripId = request.params.tripId;
    const parsed = TripIdParamSchema.safeParse({ tripId: rawTripId });
    if (!parsed.success) {
      throw new BadRequestException({
        errorCode: 'VALIDATION_FAILED',
        message: 'Invalid tripId',
        fields: parsed.error.issues.map((issue) => ({
          field: 'tripId',
          message: issue.message,
        })),
      });
    }
    const tripId = parsed.data.tripId;
    const authorization = await this.authorizationAdapter.authorizeTripTracking(user, tripId);
    if (!authorization.allowed) {
      if (authorization.error === 'TRIP_NOT_FOUND') {
        throw new NotFoundException({
          errorCode: 'TRIP_NOT_FOUND',
          detail: `Trip ${tripId} not found`,
        });
      }
      if (authorization.error === 'TRACKING_AUTH_UNAVAILABLE') {
        throw new ServiceUnavailableException({
          errorCode: 'TRACKING_AUTH_UNAVAILABLE',
          detail: 'Tracking authorization provider is unavailable',
        });
      }

      throw new ForbiddenException({
        errorCode: authorization.error ?? 'ACCESS_DENIED',
        detail: 'User is not allowed to access tracking data for this trip',
      });
    }

    (request as unknown as Record<string, unknown>).user = user;
    return true;
  }

  private readBearerToken(authorizationHeader: string | undefined): string | undefined {
    if (!authorizationHeader?.startsWith('Bearer ')) return undefined;
    const token = authorizationHeader.slice('Bearer '.length).trim();
    return token.length > 0 ? token : undefined;
  }
}
