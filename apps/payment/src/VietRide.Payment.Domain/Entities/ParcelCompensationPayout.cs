using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Payment.Domain.Entities;

public sealed class ParcelCompensationPayout : BaseEntity<Guid>
{
    public Guid ClaimId { get; private set; }
    public Guid ParcelId { get; private set; }
    public Guid TripId { get; private set; }
    public Guid OperatorId { get; private set; }
    public Guid BeneficiaryUserId { get; private set; }
    public long AmountVnd { get; private set; }
    public ParcelCompensationPayoutStatus Status { get; private set; }
    public ParcelCompensationFundingSource? FundingSource { get; private set; }
    public Guid? WalletTransactionId { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public Guid? SourceEventId { get; private set; }
    public Guid? PaidEventId { get; private set; }

    private ParcelCompensationPayout()
    {
    }

    public static ParcelCompensationPayout Create(
        Guid claimId,
        Guid parcelId,
        Guid tripId,
        Guid operatorId,
        Guid beneficiaryUserId,
        long amountVnd,
        Guid? sourceEventId = null)
    {
        if (claimId == Guid.Empty || parcelId == Guid.Empty || tripId == Guid.Empty
            || operatorId == Guid.Empty || beneficiaryUserId == Guid.Empty)
            throw new ArgumentException("Payout identity fields are required.");
        if (amountVnd <= 0)
            throw new ArgumentOutOfRangeException(nameof(amountVnd));
        if (sourceEventId == Guid.Empty)
            throw new ArgumentException("Source event id cannot be empty.", nameof(sourceEventId));

        return new ParcelCompensationPayout
        {
            Id = Guid.NewGuid(),
            ClaimId = claimId,
            ParcelId = parcelId,
            TripId = tripId,
            OperatorId = operatorId,
            BeneficiaryUserId = beneficiaryUserId,
            AmountVnd = amountVnd,
            SourceEventId = sourceEventId,
            Status = ParcelCompensationPayoutStatus.PENDING,
        };
    }

    public void EnsureSourceEvent(Guid sourceEventId)
    {
        if (sourceEventId == Guid.Empty)
            throw new ArgumentException("Source event id is required.", nameof(sourceEventId));
        if (SourceEventId.HasValue && SourceEventId != sourceEventId)
            throw new InvalidOperationException("Compensation payout source event is immutable.");
        SourceEventId = sourceEventId;
    }

    public void MarkFundingPending()
    {
        if (Status == ParcelCompensationPayoutStatus.PAID)
            throw new InvalidOperationException("A paid payout cannot return to funding pending.");
        Status = ParcelCompensationPayoutStatus.FUNDING_PENDING;
    }

    public void MarkPaid(
        ParcelCompensationFundingSource fundingSource,
        Guid walletTransactionId,
        DateTimeOffset paidAt)
    {
        if (walletTransactionId == Guid.Empty)
            throw new ArgumentException("Wallet transaction id is required.", nameof(walletTransactionId));
        Status = ParcelCompensationPayoutStatus.PAID;
        FundingSource = fundingSource;
        WalletTransactionId = walletTransactionId;
        PaidAt = paidAt;
    }

    public void MarkPaidEventEnqueued(Guid paidEventId)
    {
        if (Status != ParcelCompensationPayoutStatus.PAID)
            throw new InvalidOperationException("Only a paid compensation can publish a paid event.");
        if (paidEventId == Guid.Empty)
            throw new ArgumentException("Paid event id is required.", nameof(paidEventId));
        if (PaidEventId.HasValue && PaidEventId != paidEventId)
            throw new InvalidOperationException("Paid compensation event is immutable.");
        PaidEventId = paidEventId;
    }
}
