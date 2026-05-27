import { jwtVerify } from 'jose';
import { InternalJwtSigner } from './internal-jwt.signer';

describe('InternalJwtSigner', () => {
  const secret = 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa';
  const secretBytes = new TextEncoder().encode(secret);

  it('throws when secret is shorter than 32 chars', () => {
    expect(() => new InternalJwtSigner('too-short', 120)).toThrow(/≥32 chars/);
  });

  it('throws when secret is empty', () => {
    expect(() => new InternalJwtSigner('', 120)).toThrow();
  });

  it('produces a verifiable HS256 token with expected claims', async () => {
    const signer = new InternalJwtSigner(secret, 120);
    const token = await signer.sign({ sub: 'user-123', role: 'PASSENGER', reqId: 'req-abc' });

    const { payload, protectedHeader } = await jwtVerify(token, secretBytes, {
      issuer: 'vietride-gateway',
      audience: 'vietride-internal',
    });

    expect(protectedHeader.alg).toBe('HS256');
    expect(payload.sub).toBe('user-123');
    expect(payload.role).toBe('PASSENGER');
    expect(payload.reqId).toBe('req-abc');
    expect(payload.iss).toBe('vietride-gateway');
    expect(payload.aud).toBe('vietride-internal');
  });

  it('exp claim is approximately ttlSec from now', async () => {
    const signer = new InternalJwtSigner(secret, 120);
    const before = Math.floor(Date.now() / 1000);
    const token = await signer.sign({ sub: 'anonymous', reqId: 'req-1' });
    const { payload } = await jwtVerify(token, secretBytes);

    expect(payload.exp).toBeDefined();
    expect(payload.exp!).toBeGreaterThanOrEqual(before + 119);
    expect(payload.exp!).toBeLessThanOrEqual(before + 121);
  });

  it('different signer instances with same secret produce verifiable tokens', async () => {
    const s1 = new InternalJwtSigner(secret, 60);
    const s2 = new InternalJwtSigner(secret, 60);
    const t1 = await s1.sign({ sub: 'a', reqId: 'r' });
    await expect(jwtVerify(t1, secretBytes)).resolves.toBeDefined();
    const t2 = await s2.sign({ sub: 'b', reqId: 'r' });
    await expect(jwtVerify(t2, secretBytes)).resolves.toBeDefined();
  });
});
