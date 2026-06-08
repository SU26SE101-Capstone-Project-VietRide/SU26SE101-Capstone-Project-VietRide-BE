import { Inject, Injectable } from '@nestjs/common';
import { SignJWT } from 'jose';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';

export interface TrackingInternalJwtClaims {
  sub: string;
  role?: string;
  operatorId?: string;
  reqId: string;
  callerService?: string;
}

@Injectable()
export class TrackingInternalJwtSigner {
  private readonly secret: Uint8Array;

  constructor(@Inject(ENV_TOKEN) private readonly env: Env) {
    const secret = env.INTERNAL_JWT_SECRET;
    if (!secret || secret.length < 32) {
      throw new Error('INTERNAL_JWT_SECRET must be at least 32 chars');
    }
    this.secret = new TextEncoder().encode(secret);
  }

  async sign(claims: TrackingInternalJwtClaims): Promise<string> {
    return new SignJWT({ callerService: 'tracking', ...claims })
      .setProtectedHeader({ alg: 'HS256', typ: 'JWT' })
      .setIssuer('vietride-gateway')
      .setAudience('vietride-internal')
      .setIssuedAt()
      .setExpirationTime(`${this.env.INTERNAL_JWT_TTL_SEC}s`)
      .sign(this.secret);
  }
}
