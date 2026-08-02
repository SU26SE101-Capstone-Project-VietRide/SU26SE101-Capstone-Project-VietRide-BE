import { Inject, Injectable, ServiceUnavailableException } from '@nestjs/common';
import { SignJWT } from 'jose';
import pino from 'pino';
import { z } from 'zod';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';

const INTERNAL_AUTH_HEADER = 'X-Internal-Auth';
const INTERNAL_JWT_ISSUER = 'vietride-gateway';
const INTERNAL_JWT_AUDIENCE = 'vietride-internal';
const INTERNAL_JWT_CLOCK_SKEW_SECONDS = 5;
const IDENTITY_LOOKUP_TIMEOUT_MS = 5_000;
const ELIGIBLE_STATUSES = new Set(['ACTIVE', 'PENDING_PAYMENT']);

const identitySubscriptionResponseSchema = z
  .object({
    operatorId: z.string().uuid(),
    status: z.enum(['PENDING_APPROVAL', 'ACTIVE', 'EXPIRED', 'CANCELLED', 'PENDING_PAYMENT']),
    plan: z
      .object({
        modules: z
          .object({
            enableRag: z.boolean(),
          })
          .passthrough(),
      })
      .passthrough(),
  })
  .passthrough();

const logger = pino({ name: 'IdentitySubscriptionEntitlementClient' });

export interface RagSubscriptionEntitlement {
  operatorId: string;
  status: 'ACTIVE' | 'PENDING_PAYMENT';
  enableRag: boolean;
}

@Injectable()
export class IdentitySubscriptionEntitlementClient {
  constructor(@Inject(ENV_TOKEN) private readonly env: Env) {}

  async get(operatorId: string): Promise<RagSubscriptionEntitlement> {
    const url = new URL(
      `/internal/v1/operators/${encodeURIComponent(operatorId)}/subscription`,
      this.env.IDENTITY_INTERNAL_BASE_URL,
    );

    try {
      const response = await fetch(url, {
        headers: { [INTERNAL_AUTH_HEADER]: `Bearer ${await this.signInternalJwt()}` },
        signal: AbortSignal.timeout(IDENTITY_LOOKUP_TIMEOUT_MS),
      });
      if (!response.ok) {
        logger.warn(
          { statusCode: response.status },
          'Identity subscription entitlement lookup failed',
        );
        this.throwUnavailable();
      }

      const subscription = identitySubscriptionResponseSchema.parse(await response.json());
      if (subscription.operatorId !== operatorId || !ELIGIBLE_STATUSES.has(subscription.status)) {
        this.throwUnavailable();
      }

      return {
        operatorId: subscription.operatorId,
        status: subscription.status as RagSubscriptionEntitlement['status'],
        enableRag: subscription.plan.modules.enableRag,
      };
    } catch (error) {
      if (error instanceof ServiceUnavailableException) throw error;
      logger.warn(
        { errorType: error instanceof Error ? error.name : 'UnknownError' },
        'Identity subscription entitlement is unavailable',
      );
      this.throwUnavailable();
    }
  }

  private async signInternalJwt(): Promise<string> {
    if (!this.env.INTERNAL_JWT_SECRET) this.throwUnavailable();

    return new SignJWT({ callerService: 'rag' })
      .setProtectedHeader({ alg: 'HS256', typ: 'JWT' })
      .setSubject('rag-service')
      .setIssuer(INTERNAL_JWT_ISSUER)
      .setAudience(INTERNAL_JWT_AUDIENCE)
      .setIssuedAt()
      .setNotBefore(`${INTERNAL_JWT_CLOCK_SKEW_SECONDS}s ago`)
      .setExpirationTime(`${this.env.INTERNAL_JWT_TTL_SEC}s`)
      .sign(new TextEncoder().encode(this.env.INTERNAL_JWT_SECRET));
  }

  private throwUnavailable(): never {
    throw new ServiceUnavailableException({
      errorCode: 'UPSTREAM_UNAVAILABLE',
      detail: 'Identity subscription entitlement is temporarily unavailable',
    });
  }
}
