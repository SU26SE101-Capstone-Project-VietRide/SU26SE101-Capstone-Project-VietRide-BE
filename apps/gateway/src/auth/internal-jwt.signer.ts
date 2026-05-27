import { SignJWT } from 'jose';

/**
 * Mints a short-lived (TTL_SEC, default 120s) HS256 JWT for the X-Internal-Auth header.
 * Carries minimal claims so downstream .NET services can identify the caller without
 * re-validating the original User Access Token.
 *
 * Per BACKEND_SOURCE_OF_TRUTH 5.3 + 3.4.2.
 */
export class InternalJwtSigner {
  private readonly secret: Uint8Array;
  constructor(secret: string, private readonly ttlSec: number) {
    if (!secret || secret.length < 32) {
      throw new Error('INTERNAL_JWT_SECRET must be ≥32 chars');
    }
    this.secret = new TextEncoder().encode(secret);
  }

  async sign(claims: InternalJwtClaims): Promise<string> {
    // Default callerService='gateway' — overridable when an internal .NET service
    // mints its own token to call a peer (per BACKEND_SOURCE_OF_TRUTH §6.4).
    const enriched = { callerService: 'gateway', ...claims };
    return new SignJWT({ ...enriched })
      .setProtectedHeader({ alg: 'HS256', typ: 'JWT' })
      .setIssuer('vietride-gateway')
      .setAudience('vietride-internal')
      .setIssuedAt()
      .setExpirationTime(`${this.ttlSec}s`)
      .sign(this.secret);
  }
}

export interface InternalJwtClaims {
  /** User id from validated User Access Token (or 'anonymous' for public endpoints). */
  sub: string;
  /** RBAC role. */
  role?: string;
  /** Operator scope for tenant isolation. */
  operatorId?: string;
  /** Original request id (correlation). */
  reqId: string;
  /**
   * Caller service identifier. Defaults to 'gateway' when minted at the edge.
   * Per BACKEND_SOURCE_OF_TRUTH §6.4 — used by downstream services to audit
   * who initiated the call when service-to-service hops chain.
   */
  callerService?: string;
}
