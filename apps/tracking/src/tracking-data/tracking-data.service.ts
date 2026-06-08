import {
  ForbiddenException,
  Inject,
  Injectable,
  NotFoundException,
  ServiceUnavailableException,
  UnauthorizedException,
} from '@nestjs/common';
import {
  TRACKING_AUTHORIZATION_ADAPTER,
  TRACKING_JWT_VERIFIER,
} from '../app/tokens';
import type { TrackingUser } from '../auth/tracking-user.types';
import type { UserJwtVerifier } from '../auth/user-jwt.verifier';
import type { TrackingAuthorizationAdapter } from '../authorization/tracking-authorization.adapter';
import type { EtaQueryDto, TrailQueryDto } from './dto/tracking-data-query.dto';
import { TrackingDataRepository } from './tracking-data.repository';

export interface LatestTrackingResponseDto {
  latest: Awaited<ReturnType<TrackingDataRepository['findLatest']>>;
}

export interface TrailTrackingResponseDto {
  items: Awaited<ReturnType<TrackingDataRepository['findTrail']>>;
}

export interface EtaTrackingResponseDto {
  eta: unknown | null;
}

@Injectable()
export class TrackingDataService {
  constructor(
    private readonly repository: TrackingDataRepository,
    @Inject(TRACKING_JWT_VERIFIER) private readonly jwtVerifier: UserJwtVerifier,
    @Inject(TRACKING_AUTHORIZATION_ADAPTER)
    private readonly authorizationAdapter: TrackingAuthorizationAdapter,
  ) {}

  async getLatest(tripId: string, authorizationHeader: string | undefined): Promise<LatestTrackingResponseDto> {
    await this.authorizeRequest(authorizationHeader, tripId);
    return { latest: await this.repository.findLatest(tripId) };
  }

  async getTrail(
    tripId: string,
    query: TrailQueryDto,
    authorizationHeader: string | undefined,
  ): Promise<TrailTrackingResponseDto> {
    await this.authorizeRequest(authorizationHeader, tripId);
    return { items: await this.repository.findTrail(tripId, query) };
  }

  async getEta(
    tripId: string,
    query: EtaQueryDto,
    authorizationHeader: string | undefined,
  ): Promise<EtaTrackingResponseDto> {
    await this.authorizeRequest(authorizationHeader, tripId);
    return { eta: await this.repository.findEta(tripId, query.stopId) };
  }

  private async authorizeRequest(
    authorizationHeader: string | undefined,
    tripId: string,
  ): Promise<TrackingUser> {
    const token = this.readBearerToken(authorizationHeader);
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

    return user;
  }

  private readBearerToken(authorizationHeader: string | undefined): string | undefined {
    if (!authorizationHeader?.startsWith('Bearer ')) return undefined;
    const token = authorizationHeader.slice('Bearer '.length).trim();
    return token.length > 0 ? token : undefined;
  }
}
