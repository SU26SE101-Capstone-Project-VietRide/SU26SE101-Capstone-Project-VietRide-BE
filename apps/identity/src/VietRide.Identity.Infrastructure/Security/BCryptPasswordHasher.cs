using VietRide.Identity.Application.Abstractions;

namespace VietRide.Identity.Infrastructure.Security;

/// <summary>
/// BCrypt password hasher at work factor 12 (AGENTS.md invariant).
/// Used exclusively for <c>users.password_hash</c> — NOT for refresh tokens or OTPs.
/// </summary>
public sealed class BCryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    /// <inheritdoc />
    public string Hash(string plainText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plainText);
        return BCrypt.Net.BCrypt.HashPassword(plainText, WorkFactor);
    }

    /// <inheritdoc />
    public bool Verify(string plainText, string hash)
    {
        if (string.IsNullOrWhiteSpace(plainText) || string.IsNullOrWhiteSpace(hash))
            return false;

        return BCrypt.Net.BCrypt.Verify(plainText, hash);
    }
}
