import { Inject, Injectable, UnauthorizedException } from '@nestjs/common';
import { SignJWT } from 'jose';
import { z } from 'zod';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import {
  INTERNAL_AUTH_HEADER,
  INTERNAL_JWT_AUDIENCE,
  INTERNAL_JWT_ISSUER,
} from './fcm-push.constants';

const IDENTITY_LOOKUP_TIMEOUT_MS = 5_000;
const SystemAdminRecipientResponseSchema = z.array(z.string().uuid());

@Injectable()
export class IdentitySystemAdminRecipientProvider {
  constructor(@Inject(ENV_TOKEN) private readonly env: Env) {}

  async resolveSystemAdminRecipientUserIds(): Promise<string[]> {
    const response = await fetch(
      new URL(
        '/internal/v1/users/system-admin-recipient-ids',
        this.env.IDENTITY_INTERNAL_BASE_URL,
      ),
      {
        headers: { [INTERNAL_AUTH_HEADER]: `Bearer ${await this.signInternalJwt()}` },
        signal: AbortSignal.timeout(IDENTITY_LOOKUP_TIMEOUT_MS),
      },
    );
    if (response.status === 401) {
      throw new UnauthorizedException({
        errorCode: 'IDENTITY_INTERNAL_AUTH_FAILED',
        detail: 'Identity rejected notification internal auth token',
      });
    }
    if (!response.ok) throw new Error(`IDENTITY_SYSTEM_ADMIN_LOOKUP_FAILED_${response.status}`);
    return [...new Set(SystemAdminRecipientResponseSchema.parse(await response.json()))];
  }

  private async signInternalJwt(): Promise<string> {
    if (!this.env.INTERNAL_JWT_SECRET) {
      throw new Error('INTERNAL_JWT_SECRET_REQUIRED_FOR_SYSTEM_ADMIN_LOOKUP');
    }
    return new SignJWT({})
      .setProtectedHeader({ alg: 'HS256', typ: 'JWT' })
      .setSubject('notification-service')
      .setIssuer(INTERNAL_JWT_ISSUER)
      .setAudience(INTERNAL_JWT_AUDIENCE)
      .setIssuedAt()
      .setExpirationTime(`${this.env.INTERNAL_JWT_TTL_SEC}s`)
      .sign(new TextEncoder().encode(this.env.INTERNAL_JWT_SECRET));
  }
}
