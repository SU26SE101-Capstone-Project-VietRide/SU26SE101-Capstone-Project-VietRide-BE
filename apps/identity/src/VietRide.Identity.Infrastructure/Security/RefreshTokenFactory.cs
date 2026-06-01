using System.Security.Cryptography;
using System.Text;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Domain.Entities;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Infrastructure.Security;

/// <summary>
/// Creates opaque refresh tokens with a deterministic SHA-256 hex hash for DB lookup.
/// Raw token = 32 hex chars from <c>Guid.NewGuid().ToString("N")</c> (never persisted).
/// Stored hash = lower-case hex of <c>SHA256.HashData(UTF8.GetBytes(rawToken))</c> (64 chars).
/// Per Q2 decision: SHA-256 hex (not BCrypt) enables the <c>uq_refresh_tokens_token_hash</c>
/// index lookup that BCrypt-salted hash cannot support. BCrypt is reserved for password_hash only.
/// </summary>
public sealed class RefreshTokenFactory : IRefreshTokenFactory
{
    private static readonly TimeSpan RefreshTokenTtl = TimeSpan.FromDays(30);

    private readonly IClock _clock;

    public RefreshTokenFactory(IClock clock)
    {
        _clock = clock;
    }

    /// <inheritdoc />
    public (string rawToken, RefreshToken entity) Create(
        Guid userId,
        Guid? parentTokenId,
        Guid? familyId)
    {
        // Raw token: 32 hex chars — returned to client once, never stored
        var rawToken = Guid.NewGuid().ToString("N");

        // Deterministic SHA-256 hex — stored in DB for lookup via the unique index
        var hash = ComputeSha256Hex(rawToken);

        var now = _clock.UtcNow;
        var resolvedFamilyId = familyId ?? Guid.NewGuid();

        var entity = RefreshToken.Create(
            userId: userId,
            tokenHash: hash,
            familyId: resolvedFamilyId,
            parentTokenId: parentTokenId,
            issuedAt: now,
            expiresAt: now.Add(RefreshTokenTtl));

        return (rawToken, entity);
    }

    /// <inheritdoc />
    public string ComputeHash(string rawToken) => ComputeSha256Hex(rawToken);

    public static string ComputeSha256Hex(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
