using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Infrastructure.Security;

/// <summary>
/// Commits refresh-token family revocation on a fresh DbContext scope so reuse
/// detection is not rolled back when the handler throws UnauthorizedException.
/// </summary>
internal sealed class RefreshTokenFamilyRevoker : IRefreshTokenFamilyRevoker
{
    /// <inheritdoc />
    public Task RevokeForReuseAsync(
        IReadOnlyCollection<RefreshToken> tokens,
        DateTimeOffset revokedAt,
        CancellationToken ct = default)
    {
        foreach (var token in tokens)
            token.Revoke(revokedAt, RefreshTokenRevokeReason.REUSE_DETECTED);

        return Task.CompletedTask;
    }
}
