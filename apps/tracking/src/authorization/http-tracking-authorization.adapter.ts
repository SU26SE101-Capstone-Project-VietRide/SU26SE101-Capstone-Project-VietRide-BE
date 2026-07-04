import { Inject, Injectable } from '@nestjs/common';
import pino from 'pino';
import { z } from 'zod';
import { randomUUID } from 'crypto';
import { ENV_TOKEN } from '../app/tokens';
import type { TrackingUser } from '../auth/tracking-user.types';
import type { Env } from '../config/env.schema';
import type {
  TrackingAuthorizationAdapter,
  TrackingAuthorizationResult,
  TrackingScope,
} from './tracking-authorization.adapter';
import {
  TrackingInternalJwtClaims,
  TrackingInternalJwtSigner,
} from './tracking-internal-jwt.signer';

const INTERNAL_AUTH_HEADER = 'X-Internal-Auth';
const BEARER_PREFIX = 'Bearer ';
const HTTP_NOT_FOUND = 404;
const HTTP_FORBIDDEN = 403;
const HTTP_UNAUTHORIZED = 401;
const HTTP_SERVER_ERROR_MIN = 500;

const TrackingScopeSchema = z.enum([
  'BOOKING_OWNER',
  'DRIVER',
  'ASSISTANT',
  'OPERATOR',
  'PARCEL_SENDER',
  'PARCEL_RECIPIENT',
]);
const TrackingAuthorizationErrorSchema = z.enum([
  'TRIP_NOT_FOUND',
  'ACCESS_DENIED',
  'TRACKING_TRIP_NOT_ACTIVE',
  'TRACKING_AUTH_UNAVAILABLE',
]);

const DownstreamAuthorizationSchema = z.object({
  allowed: z.boolean(),
  scope: TrackingScopeSchema.nullish().transform((value) => value ?? undefined),
  error: TrackingAuthorizationErrorSchema.nullish().transform((value) => value ?? undefined),
});

const ApiEnvelopeAuthorizationSchema = z.object({
  success: z.boolean(),
  data: DownstreamAuthorizationSchema.optional(),
  error: z
    .object({
      code: z.string(),
    })
    .optional(),
});

interface ProviderRequest {
  baseUrl: string;
  path: string;
  expectedScopes: TrackingScope[];
  user: TrackingUser;
  tripId: string;
}

@Injectable()
export class HttpTrackingAuthorizationAdapter implements TrackingAuthorizationAdapter {
  private readonly logger = pino({ name: HttpTrackingAuthorizationAdapter.name });

  constructor(
    @Inject(ENV_TOKEN) private readonly env: Env,
    private readonly signer: TrackingInternalJwtSigner,
  ) {}

  async authorizeTripTracking(user: TrackingUser, tripId: string): Promise<TrackingAuthorizationResult> {
    const requests = this.buildProviderRequests(user, tripId);
    if (requests.length === 0) {
      return { allowed: false, error: 'ACCESS_DENIED' };
    }

    let tripNotFound = false;
    let unavailable = false;

    for (const request of requests) {
      const result = await this.requestProvider(request);
      if (result.allowed) return result;
      if (result.error === 'TRIP_NOT_FOUND') tripNotFound = true;
      if (result.error === 'TRACKING_AUTH_UNAVAILABLE') unavailable = true;
    }

    if (unavailable) return { allowed: false, error: 'TRACKING_AUTH_UNAVAILABLE' };
    if (tripNotFound) return { allowed: false, error: 'TRIP_NOT_FOUND' };
    return { allowed: false, error: 'ACCESS_DENIED' };
  }

  private buildProviderRequests(user: TrackingUser, tripId: string): ProviderRequest[] {
    switch (user.role) {
      case 'DRIVER':
        return [
          {
            baseUrl: this.env.TRIP_SERVICE_BASE_URL,
            path: this.env.TRIP_TRACKING_AUTH_PATH,
            expectedScopes: ['DRIVER'],
            user,
            tripId,
          },
        ];
      case 'ASSISTANT':
        return [
          {
            baseUrl: this.env.TRIP_SERVICE_BASE_URL,
            path: this.env.TRIP_TRACKING_AUTH_PATH,
            expectedScopes: ['ASSISTANT'],
            user,
            tripId,
          },
        ];
      case 'OPERATOR_ADMIN':
      case 'OPERATOR_STAFF':
        return [
          {
            baseUrl: this.env.TRIP_SERVICE_BASE_URL,
            path: this.env.TRIP_TRACKING_AUTH_PATH,
            expectedScopes: ['OPERATOR'],
            user,
            tripId,
          },
        ];
      case 'PASSENGER':
        return [
          {
            baseUrl: this.env.BOOKING_SERVICE_BASE_URL,
            path: this.env.BOOKING_TRACKING_AUTH_PATH,
            expectedScopes: ['BOOKING_OWNER'],
            user,
            tripId,
          },
          {
            baseUrl: this.env.PARCEL_SERVICE_BASE_URL,
            path: this.env.PARCEL_TRACKING_AUTH_PATH,
            expectedScopes: ['PARCEL_SENDER', 'PARCEL_RECIPIENT'],
            user,
            tripId,
          },
        ];
      default:
        return [];
    }
  }

  private async requestProvider(request: ProviderRequest): Promise<TrackingAuthorizationResult> {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), this.env.TRACKING_AUTH_HTTP_TIMEOUT_MS);

    try {
      const claims: TrackingInternalJwtClaims = {
        sub: request.user.userId,
        role: request.user.role,
        reqId: randomUUID(),
      };
      if (request.user.operatorId) {
        claims.operatorId = request.user.operatorId;
      }
      const token = await this.signer.sign(claims);
      const response = await fetch(this.buildUrl(request), {
        method: 'GET',
        headers: {
          [INTERNAL_AUTH_HEADER]: `${BEARER_PREFIX}${token}`,
        },
        signal: controller.signal,
      });

      const result = await this.mapResponse(response, request.expectedScopes);
      if (!result.allowed && (result.error === 'ACCESS_DENIED' || result.error === 'TRIP_NOT_FOUND')) {
        this.logger.info(
          {
            tripId: request.tripId,
            userId: request.user.userId,
            role: request.user.role,
            error: result.error,
          },
          'Tracking authorization denied by downstream',
        );
      }
      return result;
    } catch (error) {
      this.logger.warn({ err: error, tripId: request.tripId }, 'Tracking authorization downstream unavailable');
      return { allowed: false, error: 'TRACKING_AUTH_UNAVAILABLE' };
    } finally {
      clearTimeout(timeout);
    }
  }

  private buildUrl(request: ProviderRequest): string {
    const path = request.path.replace(':tripId', encodeURIComponent(request.tripId));
    const url = new URL(path, request.baseUrl);
    url.searchParams.set('userId', request.user.userId);
    url.searchParams.set('role', request.user.role);
    if (request.user.operatorId) {
      url.searchParams.set('operatorId', request.user.operatorId);
    }
    return url.toString();
  }

  private async mapResponse(response: Response, expectedScopes: TrackingScope[]): Promise<TrackingAuthorizationResult> {
    if (response.status === HTTP_NOT_FOUND) {
      return { allowed: false, error: 'TRIP_NOT_FOUND' };
    }
    if (response.status === HTTP_FORBIDDEN) {
      return { allowed: false, error: 'ACCESS_DENIED' };
    }
    if (response.status === HTTP_UNAUTHORIZED || response.status >= HTTP_SERVER_ERROR_MIN) {
      return { allowed: false, error: 'TRACKING_AUTH_UNAVAILABLE' };
    }

    const parsed = this.parseBody(await response.json().catch(() => null));
    if (!parsed.allowed) return parsed;
    if (!parsed.scope || !expectedScopes.includes(parsed.scope)) {
      return { allowed: false, error: 'ACCESS_DENIED' };
    }

    return parsed;
  }

  private parseBody(body: unknown): TrackingAuthorizationResult {
    const direct = DownstreamAuthorizationSchema.safeParse(body);
    if (direct.success) return this.toAuthorizationResult(direct.data);

    const envelope = ApiEnvelopeAuthorizationSchema.safeParse(body);
    if (!envelope.success) {
      return { allowed: false, error: 'TRACKING_AUTH_UNAVAILABLE' };
    }
    if (envelope.data.success && envelope.data.data) {
      return this.toAuthorizationResult(envelope.data.data);
    }
    if (envelope.data.error?.code === 'TRIP_NOT_FOUND') {
      return { allowed: false, error: 'TRIP_NOT_FOUND' };
    }
    if (envelope.data.error?.code === 'ACCESS_DENIED') {
      return { allowed: false, error: 'ACCESS_DENIED' };
    }
    return { allowed: false, error: 'TRACKING_AUTH_UNAVAILABLE' };
  }

  private toAuthorizationResult(result: z.infer<typeof DownstreamAuthorizationSchema>): TrackingAuthorizationResult {
    if (result.allowed) {
      return result.scope ? { allowed: true, scope: result.scope } : { allowed: true };
    }
    return result.error ? { allowed: false, error: result.error } : { allowed: false };
  }
}
