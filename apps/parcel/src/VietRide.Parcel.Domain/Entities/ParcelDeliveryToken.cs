using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Domain.Entities;

public sealed class ParcelDeliveryToken : BaseEntity<Guid>
{
    public Guid ParcelId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? IssuedByUserId { get; private set; }
    public ParcelDeliveryTokenIssueReason IssueReason { get; private set; }

    private ParcelDeliveryToken()
    {
    }

    public static ParcelDeliveryToken Issue(
        Guid parcelId,
        string tokenHash,
        DateTimeOffset expiresAt,
        Guid? issuedByUserId,
        ParcelDeliveryTokenIssueReason issueReason,
        DateTimeOffset issuedAt)
    {
        if (parcelId == Guid.Empty)
        {
            throw new ArgumentException("Parcel id is required.", nameof(parcelId));
        }

        if (!IsSha256Hex(tokenHash))
        {
            throw new ArgumentException(
                "Delivery token hash must be 64 lowercase hexadecimal characters.",
                nameof(tokenHash));
        }

        if (expiresAt <= issuedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiresAt),
                "Delivery token expiry must be after its issue time.");
        }

        return new ParcelDeliveryToken
        {
            Id = Guid.NewGuid(),
            ParcelId = parcelId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            IssuedByUserId = issuedByUserId,
            IssueReason = issueReason,
            CreatedAt = issuedAt,
            UpdatedAt = issuedAt,
        };
    }

    public void Revoke(DateTimeOffset revokedAt)
    {
        if (RevokedAt.HasValue)
        {
            return;
        }

        RevokedAt = revokedAt;
        UpdatedAt = revokedAt;
    }

    private static bool IsSha256Hex(string value)
        => !string.IsNullOrEmpty(value)
            && value.Length == 64
            && value.All(character =>
                character is >= '0' and <= '9'
                || character is >= 'a' and <= 'f');
}
