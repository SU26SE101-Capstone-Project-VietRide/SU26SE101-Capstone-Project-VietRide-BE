import { Inject, Injectable, UnauthorizedException } from '@nestjs/common';
import { createHash, createHmac, timingSafeEqual } from 'node:crypto';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';

const TOKEN_VERSION = 'v1' as const;
const TOKEN_MAX_LENGTH = 160;
const HMAC_SIGNATURE_LENGTH = 43;
const MINIMUM_SECRET_BYTES = 32;
const UUID_V4_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const SIGNATURE_PATTERN = /^[A-Za-z0-9_-]+$/;

export interface TripShareIssuedToken {
  token: string;
  tokenHash: string;
}

export interface TripShareVerifiedToken {
  version: typeof TOKEN_VERSION;
  grantId: string;
  tokenHash: string;
}

@Injectable()
export class TripShareTokenCodec {
  private readonly secret: Buffer;

  constructor(@Inject(ENV_TOKEN) env: Env) {
    this.secret = Buffer.from(env.TRACKING_SHARE_TOKEN_SECRET, 'utf8');
    if (this.secret.byteLength < MINIMUM_SECRET_BYTES) {
      throw new Error('TRACKING_SHARE_TOKEN_SECRET must be at least 32 bytes');
    }
  }

  create(grantId: string): TripShareIssuedToken {
    const normalizedGrantId = this.normalizeGrantId(grantId);
    const canonical = `${TOKEN_VERSION}.${normalizedGrantId}`;
    const signature = createHmac('sha256', this.secret).update(canonical, 'ascii').digest('base64url');
    const token = `${canonical}.${signature}`;

    return { token, tokenHash: this.hash(token) };
  }

  verify(token: string): TripShareVerifiedToken {
    if (!token || token.length > TOKEN_MAX_LENGTH) this.throwInvalid();

    const segments = token.split('.');
    if (segments.length !== 3) this.throwInvalid();
    const [version, rawGrantId, signature] = segments;
    if (version !== TOKEN_VERSION || !rawGrantId || !signature) this.throwInvalid();
    if (signature.length !== HMAC_SIGNATURE_LENGTH || !SIGNATURE_PATTERN.test(signature)) {
      this.throwInvalid();
    }

    let grantId: string;
    try {
      grantId = this.normalizeGrantId(rawGrantId);
    } catch {
      this.throwInvalid();
    }

    const expectedSignature = this.create(grantId).token.split('.')[2] ?? '';
    const supplied = Buffer.from(signature, 'ascii');
    const expected = Buffer.from(expectedSignature, 'ascii');
    if (supplied.byteLength !== expected.byteLength || !timingSafeEqual(supplied, expected)) {
      this.throwInvalid();
    }

    return { version: TOKEN_VERSION, grantId, tokenHash: this.hash(token) };
  }

  private normalizeGrantId(grantId: string): string {
    if (!UUID_V4_PATTERN.test(grantId)) throw new Error('Trip share grant id must be a UUID v4');
    return grantId.toLowerCase();
  }

  private hash(token: string): string {
    return createHash('sha256').update(token, 'utf8').digest('hex');
  }

  private throwInvalid(): never {
    throw new UnauthorizedException({
      errorCode: 'TRACKING_SHARE_TOKEN_INVALID',
      detail: 'The trip share token is invalid',
    });
  }
}
