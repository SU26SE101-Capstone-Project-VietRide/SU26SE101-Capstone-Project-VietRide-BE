import { Inject, Injectable } from '@nestjs/common';
import { createRemoteJWKSet, importSPKI, jwtVerify, type KeyLike } from 'jose';
import type { Env } from '../config/env.schema';
import { ENV_TOKEN } from '../app/tokens';
import type { TrackingUser } from './tracking-user.types';

// Small tolerance for clock drift between Identity and Tracking containers.
const JWT_CLOCK_TOLERANCE_SECONDS = 5;

export interface UserJwtVerifier {
  verify(token: string): Promise<TrackingUser>;
}

@Injectable()
export class JoseUserJwtVerifier implements UserJwtVerifier {
  private readonly remoteJwks: ReturnType<typeof createRemoteJWKSet>;
  private readonly localPublicKey: Promise<KeyLike> | undefined;

  constructor(@Inject(ENV_TOKEN) private readonly env: Env) {
    this.remoteJwks = createRemoteJWKSet(new URL(env.JWT_PUBLIC_KEY_URL));
    this.localPublicKey = env.USER_JWT_PUBLIC_KEY
      ? importSPKI(env.USER_JWT_PUBLIC_KEY.replace(/\\n/g, '\n'), 'RS256')
      : undefined;
  }

  async verify(token: string): Promise<TrackingUser> {
    const result = this.localPublicKey
      ? await jwtVerify(token, await this.localPublicKey, {
          issuer: this.env.JWT_ISSUER,
          audience: this.env.JWT_AUDIENCE,
          algorithms: ['RS256'],
          clockTolerance: JWT_CLOCK_TOLERANCE_SECONDS,
        })
      : await jwtVerify(token, this.remoteJwks, {
          issuer: this.env.JWT_ISSUER,
          audience: this.env.JWT_AUDIENCE,
          algorithms: ['RS256'],
          clockTolerance: JWT_CLOCK_TOLERANCE_SECONDS,
        });

    const userId = result.payload.sub;
    if (!userId) {
      throw new Error('UNAUTHORIZED');
    }

    const role = this.readStringClaim(result.payload.role) ?? this.readStringClaim(result.payload.roles);
    if (!role) {
      throw new Error('UNAUTHORIZED');
    }

    const user: TrackingUser = {
      userId,
      role,
    };
    const operatorId = this.readStringClaim(result.payload.operatorId);
    if (operatorId) user.operatorId = operatorId;

    return user;
  }

  private readStringClaim(value: unknown): string | undefined {
    if (typeof value === 'string' && value.length > 0) return value;
    if (Array.isArray(value) && typeof value[0] === 'string' && value[0].length > 0) return value[0];
    return undefined;
  }
}
