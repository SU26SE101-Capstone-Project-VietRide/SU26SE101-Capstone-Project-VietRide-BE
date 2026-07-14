import { Inject, Injectable, UnauthorizedException } from '@nestjs/common';
import { SignJWT } from 'jose';
import pino from 'pino';
import { z } from 'zod';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import {
  INTERNAL_AUTH_HEADER,
  INTERNAL_JWT_AUDIENCE,
  INTERNAL_JWT_ISSUER,
} from './fcm-push.constants';
import type { OperatorRecipientProvider } from './operator-recipient.provider';

const OperatorRecipientResponseSchema = z.array(z.string().uuid());
const INTERNAL_JWT_CLOCK_SKEW_SECONDS = 5;
const IDENTITY_LOOKUP_TIMEOUT_MS = 5_000;

@Injectable()
export class IdentityOperatorRecipientProvider implements OperatorRecipientProvider {
  private readonly logger = pino({ name: IdentityOperatorRecipientProvider.name });

  constructor(@Inject(ENV_TOKEN) private readonly env: Env) {}

  async resolveOperatorRecipientUserIds(operatorId: string): Promise<string[]> {
    const url = new URL(
      `/internal/v1/operators/${operatorId}/recipient-users`,
      this.env.IDENTITY_INTERNAL_BASE_URL,
    );
    const token = await this.signInternalJwt();
    const response = await fetch(url, {
      headers: {
        [INTERNAL_AUTH_HEADER]: `Bearer ${token}`,
      },
      signal: AbortSignal.timeout(IDENTITY_LOOKUP_TIMEOUT_MS),
    });

    if (response.status === 401) {
      throw new UnauthorizedException({
        errorCode: 'IDENTITY_INTERNAL_AUTH_FAILED',
        detail: 'Identity rejected notification internal auth token',
      });
    }

    if (!response.ok) {
      this.logger.warn(
        { operatorId, statusCode: response.status },
        'Identity operator-recipient lookup failed',
      );
      throw new Error(`IDENTITY_OPERATOR_RECIPIENT_LOOKUP_FAILED_${response.status}`);
    }

    return OperatorRecipientResponseSchema.parse(await response.json());
  }

  async resolveOperatorCrewUserIds(operatorId: string): Promise<string[]> {
    const url = new URL(
      `/internal/v1/operators/${operatorId}/crew-user-ids`,
      this.env.IDENTITY_INTERNAL_BASE_URL,
    );
    const response = await fetch(url, {
      headers: { [INTERNAL_AUTH_HEADER]: `Bearer ${await this.signInternalJwt()}` },
      signal: AbortSignal.timeout(IDENTITY_LOOKUP_TIMEOUT_MS),
    });
    if (response.status === 401) {
      throw new UnauthorizedException({
        errorCode: 'IDENTITY_INTERNAL_AUTH_FAILED',
        detail: 'Identity rejected notification internal auth token',
      });
    }
    if (!response.ok) {
      this.logger.warn(
        { operatorId, statusCode: response.status },
        'Identity operator crew lookup failed',
      );
      throw new Error(`IDENTITY_OPERATOR_CREW_LOOKUP_FAILED_${response.status}`);
    }
    return OperatorRecipientResponseSchema.parse(await response.json());
  }
  private async signInternalJwt(): Promise<string> {
    if (!this.env.INTERNAL_JWT_SECRET) {
      throw new Error('INTERNAL_JWT_SECRET_REQUIRED_FOR_OPERATOR_RECIPIENT_LOOKUP');
    }

    const secret = new TextEncoder().encode(this.env.INTERNAL_JWT_SECRET);

    return new SignJWT({})
      .setProtectedHeader({ alg: 'HS256', typ: 'JWT' })
      .setSubject('notification-service')
      .setIssuer(INTERNAL_JWT_ISSUER)
      .setAudience(INTERNAL_JWT_AUDIENCE)
      .setIssuedAt()
      .setNotBefore(`${INTERNAL_JWT_CLOCK_SKEW_SECONDS}s ago`)
      .setExpirationTime(`${this.env.INTERNAL_JWT_TTL_SEC}s`)
      .sign(secret);
  }
}
