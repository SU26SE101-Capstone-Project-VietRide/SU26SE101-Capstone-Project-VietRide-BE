using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Identity.Application.Abstractions.Repositories;

/// <summary>
/// Repository for the <see cref="RefreshToken"/> aggregate.
/// </summary>
public interface IRefreshTokenRepository : IRepository<RefreshToken, Guid>
{
    /// <summary>
    /// Looks up a refresh token by its deterministic SHA-256 hex hash
    /// (the raw token is never persisted — only the hash).
    /// </summary>
    Task<RefreshToken?> GetByTokenHashAsync(string sha256Hex, CancellationToken ct = default);

    /// <summary>
    /// Revokes every token in the given family (sets revoked_at + revoked_reason).
    /// Used for reuse-detection: a previously-revoked token is presented → revoke the whole family.
    /// The timestamp is resolved internally via <see cref="VietRide.Shared.Kernel.Abstractions.IClock"/>.
    /// </summary>
    Task RevokeFamilyAsync(Guid familyId, RefreshTokenRevokeReason reason, CancellationToken ct = default);

    /// <summary>
    /// Revokes every active refresh token owned by <paramref name="userId"/>.
    /// Used after password reset so all existing sessions must authenticate again.
    /// </summary>
    Task RevokeActiveByUserAsync(Guid userId, RefreshTokenRevokeReason reason, CancellationToken ct = default);

    /// <summary>Revokes all active refresh tokens owned by the supplied users.</summary>
    Task RevokeActiveByUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        RefreshTokenRevokeReason reason,
        CancellationToken ct = default);
}
