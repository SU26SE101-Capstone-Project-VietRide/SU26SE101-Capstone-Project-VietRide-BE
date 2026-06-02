namespace VietRide.Identity.Application.Abstractions;

/// <summary>
/// Abstraction for password hashing. Implementation uses BCrypt with cost 12.
/// Reserved for <c>users.password_hash</c> only — NOT for refresh tokens or OTPs.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// Hashes a plain-text password using BCrypt at cost 12.
    /// </summary>
    string Hash(string plainText);

    /// <summary>
    /// Verifies a plain-text password against a stored BCrypt hash.
    /// </summary>
    bool Verify(string plainText, string hash);
}
