namespace VietRide.Identity.Application.Abstractions;

/// <summary>
/// Applies reuse revocation to refresh-token rows already locked and tracked by the
/// caller's transaction. It never creates another scope or saves independently.
/// </summary>
public interface IRefreshTokenFamilyRevoker
{
    /// <summary>
    /// Revokes every supplied token with REUSE_DETECTED. The caller owns SaveChanges/commit.
    /// </summary>
    Task RevokeForReuseAsync(
        IReadOnlyCollection<Domain.Entities.RefreshToken> tokens,
        DateTimeOffset revokedAt,
        CancellationToken ct = default);
}
