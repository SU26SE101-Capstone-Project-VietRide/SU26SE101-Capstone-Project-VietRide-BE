namespace VietRide.Identity.Infrastructure.Security;

/// <summary>
/// Configuration options for the RS256 JWT signing key.
/// Bound from <c>IdentityJwt</c> config section.
/// In production, values come from environment variables
/// <c>USER_JWT_PRIVATE_KEY</c> (PEM) and <c>USER_JWT_KID</c> (BSOT §11.3).
/// </summary>
public sealed class JwtSigningOptions
{
    public const string SectionName = "IdentityJwt";

    /// <summary>
    /// RSA private key in PEM format (includes both private and public components).
    /// Production: supplied via <c>USER_JWT_PRIVATE_KEY</c> environment variable.
    /// </summary>
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>
    /// Key identifier used in the JWT <c>kid</c> header and JWKS <c>kid</c> field.
    /// Production: supplied via <c>USER_JWT_KID</c> environment variable.
    /// </summary>
    public string Kid { get; set; } = string.Empty;
}
