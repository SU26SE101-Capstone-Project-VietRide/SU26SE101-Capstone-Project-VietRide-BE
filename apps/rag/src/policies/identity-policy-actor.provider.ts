import { Inject, Injectable } from '@nestjs/common';
import { SignJWT } from 'jose';
import pino from 'pino';
import { z } from 'zod';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import type { PolicyActorProfile } from './policies.types';

const INTERNAL_AUTH_HEADER = 'X-Internal-Auth';
const INTERNAL_JWT_ISSUER = 'vietride-gateway';
const INTERNAL_JWT_AUDIENCE = 'vietride-internal';
const INTERNAL_JWT_CLOCK_SKEW_SECONDS = 5;
const IDENTITY_LOOKUP_TIMEOUT_MS = 5_000;
const IdentityActorResponseSchema = z.array(
  z
    .object({
      id: z.string().uuid(),
      displayName: z.string().trim().min(1),
      email: z.string().email().nullable(),
      deleted: z.boolean(),
    })
    .passthrough(),
);
const logger = pino({ name: 'IdentityPolicyActorProvider' });

@Injectable()
export class IdentityPolicyActorProvider {
  constructor(@Inject(ENV_TOKEN) private readonly env: Env) {}

  async resolve(userId: string): Promise<PolicyActorProfile> {
    const url = new URL('/internal/v1/users', this.env.IDENTITY_INTERNAL_BASE_URL);
    url.searchParams.append('ids', userId);

    try {
      const response = await fetch(url, {
        headers: { [INTERNAL_AUTH_HEADER]: `Bearer ${await this.signInternalJwt()}` },
        signal: AbortSignal.timeout(IDENTITY_LOOKUP_TIMEOUT_MS),
      });
      if (!response.ok) {
        logger.warn({ statusCode: response.status }, 'Identity Policy actor lookup failed');
        throw new Error('IDENTITY_POLICY_ACTOR_LOOKUP_FAILED');
      }

      const actors = IdentityActorResponseSchema.parse(await response.json());
      const actor = actors.length === 1 && actors[0]?.id === userId ? actors[0] : undefined;
      if (!actor || actor.deleted || !actor.email) {
        throw new Error('IDENTITY_POLICY_ACTOR_INVALID');
      }

      return { displayName: actor.displayName, email: actor.email };
    } catch (error) {
      logger.warn(
        { errorType: error instanceof Error ? error.name : 'UnknownError' },
        'Identity Policy actor profile is unavailable',
      );
      throw new Error('IDENTITY_POLICY_ACTOR_UNAVAILABLE');
    }
  }

  private async signInternalJwt(): Promise<string> {
    if (!this.env.INTERNAL_JWT_SECRET) {
      throw new Error('INTERNAL_JWT_SECRET_REQUIRED_FOR_POLICY_ACTOR_LOOKUP');
    }

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
}
