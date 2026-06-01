namespace VietRide.Identity.Application.Abstractions;

/// <summary>
/// Provides the JSON Web Key Set (JWKS) for the Identity Service RS256 signing key.
/// The JWKS exposes the public key only (n, e, kid, kty=RSA, alg=RS256, use=sig).
/// </summary>
public interface IJwksProvider
{
    /// <summary>
    /// Returns the JWKS document as a serialized JSON string.
    /// Matches the shape required by <c>GET /v1/.well-known/jwks.json</c>.
    /// </summary>
    string GetJwks();
}
