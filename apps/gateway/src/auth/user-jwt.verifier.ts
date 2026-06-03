import { createRemoteJWKSet, jwtVerify, type JWTPayload } from 'jose';
import type { Env } from '../config/env.schema';

export class UserJwtVerificationError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'UserJwtVerificationError';
  }
}

export interface UserJwtVerifier {
  verifyAuthorizationHeader(auth: string | undefined): Promise<JWTPayload>;
}

export function createUserJwtVerifier(
  env: Pick<Env, 'JWT_PUBLIC_KEY_URL' | 'JWT_ISSUER' | 'JWT_AUDIENCE'>,
): UserJwtVerifier {
  const jwks = createRemoteJWKSet(new URL(env.JWT_PUBLIC_KEY_URL));

  return {
    async verifyAuthorizationHeader(auth: string | undefined): Promise<JWTPayload> {
      if (!auth?.toLowerCase().startsWith('bearer ')) {
        throw new UserJwtVerificationError('Authorization header required');
      }

      try {
        const { payload } = await jwtVerify(auth.slice(7).trim(), jwks, {
          issuer: env.JWT_ISSUER,
          audience: env.JWT_AUDIENCE,
          clockTolerance: 5,
        });

        return payload;
      } catch (err) {
        if (err instanceof UserJwtVerificationError) {
          throw err;
        }

        throw new UserJwtVerificationError(
          err instanceof Error ? err.message : 'Access token invalid or expired',
        );
      }
    },
  };
}
