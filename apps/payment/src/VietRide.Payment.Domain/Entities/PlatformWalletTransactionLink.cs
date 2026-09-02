using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Payment.Domain.Entities;

public sealed class PlatformWalletTransactionLink : BaseEntity<Guid>
{
    private PlatformWalletTransactionLink()
    {
    }

    public Guid PlatformWalletTransactionId { get; private set; }
    public Guid? OperatorId { get; private set; }
    public Guid? TripId { get; private set; }
    public PlatformWalletTransactionLinkType LinkType { get; private set; }
    public Guid? ReferenceId { get; private set; }
    public string? ReferenceCode { get; private set; }
    public long AllocatedAmount { get; private set; }

    public static PlatformWalletTransactionLink Create(
        Guid platformWalletTransactionId,
        PlatformWalletTransactionLinkType linkType,
        long allocatedAmount,
        Guid? operatorId = null,
        Guid? tripId = null,
        Guid? referenceId = null,
        string? referenceCode = null)
    {
        if (platformWalletTransactionId == Guid.Empty)
            throw new ArgumentException("Platform wallet transaction id is required.", nameof(platformWalletTransactionId));
        if (operatorId == Guid.Empty)
            throw new ArgumentException("Operator id cannot be empty.", nameof(operatorId));
        if (tripId == Guid.Empty)
            throw new ArgumentException("Trip id cannot be empty.", nameof(tripId));
        if (referenceId == Guid.Empty)
            throw new ArgumentException("Reference id cannot be empty.", nameof(referenceId));
        if (allocatedAmount < 0)
            throw new ArgumentOutOfRangeException(nameof(allocatedAmount), "Allocated amount cannot be negative.");
        if (referenceCode is not null
            && (string.IsNullOrWhiteSpace(referenceCode)
                || referenceCode.Length > 64
                || !string.Equals(referenceCode, referenceCode.Trim(), StringComparison.Ordinal)))
        {
            throw new ArgumentException("Reference code must be trimmed and at most 64 characters.", nameof(referenceCode));
        }

        return new PlatformWalletTransactionLink
        {
            Id = Guid.NewGuid(),
            PlatformWalletTransactionId = platformWalletTransactionId,
            OperatorId = operatorId,
            TripId = tripId,
            LinkType = linkType,
            ReferenceId = referenceId,
            ReferenceCode = referenceCode,
            AllocatedAmount = allocatedAmount,
        };
    }
}
