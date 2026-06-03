namespace VietRide.Identity.Application.Abstractions;

/// <summary>
/// Persists refresh-token family revocation independently of the ambient refresh
/// command transaction so reuse detection survives the 401 exception path.
/// </summary>
public interface IRefreshTokenFamilyRevoker
{
    /// <summary>
    /// Revokes every token in the family with REUSE_DETECTED and commits immediately.
    /// </summary>
    Task RevokeForReuseAsync(Guid familyId, CancellationToken ct = default);
}
