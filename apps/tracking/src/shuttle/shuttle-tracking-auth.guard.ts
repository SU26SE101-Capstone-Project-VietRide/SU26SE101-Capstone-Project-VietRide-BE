import {
  BadRequestException,
  CanActivate,
  ExecutionContext,
  ForbiddenException,
  Inject,
  Injectable,
  NotFoundException,
  ServiceUnavailableException,
  UnauthorizedException,
} from '@nestjs/common';
import type { Request } from 'express';
import { TRACKING_JWT_VERIFIER } from '../app/tokens';
import type { TrackingUser } from '../auth/tracking-user.types';
import type { UserJwtVerifier } from '../auth/user-jwt.verifier';
import { ShuttleTripIdParamSchema } from './shuttle.dto';
import { ShuttleService } from './shuttle.service';

@Injectable()
export class ShuttleTrackingAuthGuard implements CanActivate {
  constructor(
    @Inject(TRACKING_JWT_VERIFIER) private readonly jwtVerifier: UserJwtVerifier,
    private readonly shuttleService: ShuttleService,
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

    const parsed = ShuttleTripIdParamSchema.safeParse(request.params);
    if (!parsed.success) {
      throw new BadRequestException({
        errorCode: 'VALIDATION_FAILED',
        message: 'Invalid shuttleTripId',
        fields: parsed.error.issues.map((issue) => ({
          field: 'shuttleTripId',
          message: issue.message,
        })),
      });
    }

    try {
      const authorization = await this.shuttleService.getContext(user, parsed.data.shuttleTripId);
      if (!authorization.allowed) {
        throw new ForbiddenException({
          errorCode: 'TRACKING_ACCESS_DENIED',
          detail: 'User is not allowed to access this shuttle trip',
        });
      }
    } catch (error) {
      if (error instanceof ForbiddenException) throw error;
      if ((error as Error).message === 'SHUTTLE_TRIP_NOT_FOUND') {
        throw new NotFoundException({
          errorCode: 'SHUTTLE_TRIP_NOT_FOUND',
          detail: `Shuttle trip ${parsed.data.shuttleTripId} was not found`,
        });
      }
      throw new ServiceUnavailableException({
        errorCode: 'TRACKING_AUTH_UNAVAILABLE',
        detail: 'Shuttle tracking authorization provider is unavailable',
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
