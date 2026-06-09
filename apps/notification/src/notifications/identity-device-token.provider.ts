import { Inject, Injectable, UnauthorizedException } from '@nestjs/common';
import { SignJWT } from 'jose';
import pino from 'pino';
import { z } from 'zod';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import { DevicePlatform } from '../generated/notification-prisma-client';
import {
  INTERNAL_AUTH_HEADER,
  INTERNAL_JWT_AUDIENCE,
  INTERNAL_JWT_ISSUER,
} from './fcm-push.constants';
import type { DeviceTokenProvider, DeviceTokenSnapshot } from './fcm-push.types';

const DeviceTokenResponseSchema = z.array(
  z.object({
    fcmToken: z.string().trim().min(1),
    platform: z.nativeEnum(DevicePlatform),
  }),
);

const INTERNAL_JWT_CLOCK_SKEW_SECONDS = 5;

@Injectable()
export class IdentityDeviceTokenProvider implements DeviceTokenProvider {
  private readonly logger = pino({ name: IdentityDeviceTokenProvider.name });

  constructor(@Inject(ENV_TOKEN) private readonly env: Env) {}

  async listActiveDeviceTokens(userId: string): Promise<DeviceTokenSnapshot[]> {
    const url = new URL(`/internal/v1/users/${userId}/device-tokens`, this.env.IDENTITY_INTERNAL_BASE_URL);
    const token = await this.signInternalJwt();
    const response = await fetch(url, {
      headers: {
        [INTERNAL_AUTH_HEADER]: `Bearer ${token}`,
      },
    });

    if (response.status === 401) {
      throw new UnauthorizedException({
        errorCode: 'IDENTITY_INTERNAL_AUTH_FAILED',
        detail: 'Identity rejected notification internal auth token',
      });
    }

    if (!response.ok) {
      this.logger.warn({ userId, statusCode: response.status }, 'Identity device-token lookup failed');
      throw new Error(`IDENTITY_DEVICE_TOKEN_LOOKUP_FAILED_${response.status}`);
    }

    const payload = await response.json();
    const parsed = DeviceTokenResponseSchema.parse(payload);

    return parsed.map((device) => ({
      fcmToken: device.fcmToken,
      platform: device.platform,
    }));
  }

  private async signInternalJwt(): Promise<string> {
    if (!this.env.INTERNAL_JWT_SECRET) {
      throw new Error('INTERNAL_JWT_SECRET_REQUIRED_FOR_DEVICE_TOKEN_LOOKUP');
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
