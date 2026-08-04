import { UnauthorizedException } from '@nestjs/common';
import type { Env } from '../config/env.schema';
import { TripShareTokenCodec } from './trip-share-token.codec';

const SECRET = 'phase13-test-share-token-secret-32-bytes';
const GRANT_ID = '11111111-1111-4111-8111-111111111111';
const OTHER_GRANT_ID = '22222222-2222-4222-8222-222222222222';

describe('TripShareTokenCodec', () => {
  const codec = new TripShareTokenCodec({ TRACKING_SHARE_TOKEN_SECRET: SECRET } as Env);

  it('creates and verifies a deterministic v1 token', () => {
    const first = codec.create(GRANT_ID);
    const second = codec.create(GRANT_ID);

    expect(first).toEqual(second);
    expect(codec.verify(first.token)).toEqual({
      version: 'v1',
      grantId: GRANT_ID,
      tokenHash: first.tokenHash,
    });
  });

  it('creates a different token for another grant', () => {
    expect(codec.create(GRANT_ID).token).not.toBe(codec.create(OTHER_GRANT_ID).token);
  });

  it.each([
    ['', 'empty'],
    ['not-a-token', 'malformed'],
    [`v2.${GRANT_ID}.signature`, 'version'],
    ['v1.not-a-uuid.signature', 'UUID'],
    [`v1.${GRANT_ID}.${'a'.repeat(256)}`, 'overlong'],
  ])('rejects %s token input (%s)', (token) => {
    expect(() => codec.verify(token)).toThrow(UnauthorizedException);
  });

  it('rejects a tampered signature', () => {
    const issued = codec.create(GRANT_ID).token;
    const replacement = issued.endsWith('A') ? 'B' : 'A';

    expect(() => codec.verify(`${issued.slice(0, -1)}${replacement}`)).toThrow(UnauthorizedException);
  });

  it('rejects a different-length signature without throwing a timingSafeEqual range error', () => {
    expect(() => codec.verify(`v1.${GRANT_ID}.short`)).toThrow(UnauthorizedException);
  });

  it('hashes the full token as lowercase 64-character SHA-256 hex', () => {
    expect(codec.create(GRANT_ID).tokenHash).toMatch(/^[0-9a-f]{64}$/);
  });

  it('fails construction when the secret is weaker than 32 bytes', () => {
    expect(() => new TripShareTokenCodec({ TRACKING_SHARE_TOKEN_SECRET: 'too-short' } as Env))
      .toThrow('TRACKING_SHARE_TOKEN_SECRET must be at least 32 bytes');
  });
});
