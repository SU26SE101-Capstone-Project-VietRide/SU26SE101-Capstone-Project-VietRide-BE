using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Domain.Entities;

public sealed class RefreshToken : BaseEntity<Guid>
{
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public Guid FamilyId { get; private set; }
    public Guid? ParentTokenId { get; private set; }
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public RefreshTokenRevokeReason? RevokedReason { get; private set; }
    public string? UserAgent { get; private set; }
    public string? IpAddress { get; private set; }

    private RefreshToken() { }

    public static RefreshToken Create(
        Guid userId,
        string tokenHash,
        Guid familyId,
        Guid? parentTokenId,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        string? userAgent = null,
        string? ipAddress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            FamilyId = familyId,
            ParentTokenId = parentTokenId,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt,
            UserAgent = userAgent,
            IpAddress = ipAddress,
        };
    }

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTimeOffset.UtcNow;

    public void Revoke(DateTimeOffset revokedAt, RefreshTokenRevokeReason reason)
    {
        RevokedAt = revokedAt;
        RevokedReason = reason;
    }
}
