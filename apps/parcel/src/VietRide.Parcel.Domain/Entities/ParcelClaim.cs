using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Domain.Entities;

public sealed class ParcelClaim : BaseEntity<Guid>
{
    public Guid ParcelId { get; private set; }
    public Guid IncidentId { get; private set; }
    public Guid OperatorId { get; private set; }
    public Guid BeneficiaryUserId { get; private set; }
    public ParcelClaimStatus Status { get; private set; }
    public long? DeclaredValueVnd { get; private set; }
    public long? ProvenDirectLossVnd { get; private set; }
    public int CompensationRatePercent { get; private set; }
    public long PolicyCapVnd { get; private set; }
    public long CargoAwardVnd { get; private set; }
    public long FreightRefundVnd { get; private set; }
    public long TotalAwardVnd { get; private set; }
    public int PolicyVersion { get; private set; }
    public int NoProofFallbackMultiplier { get; private set; }
    public string? DecisionReason { get; private set; }
    public Guid? DecidedBy { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public Guid? PayoutReferenceId { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public string? AppealReason { get; private set; }
    public Guid? AppealedByUserId { get; private set; }
    public DateTimeOffset? AppealedAt { get; private set; }

    private ParcelClaim()
    {
    }

    public static ParcelClaim Submit(
        Guid parcelId,
        Guid incidentId,
        Guid operatorId,
        Guid beneficiaryUserId,
        long? declaredValueVnd,
        int policyVersion,
        int compensationRatePercent,
        long policyCapVnd,
        int noProofFallbackMultiplier)
    {
        if (parcelId == Guid.Empty || incidentId == Guid.Empty || operatorId == Guid.Empty || beneficiaryUserId == Guid.Empty)
            throw new ArgumentException("Parcel, incident, operator and beneficiary ids are required.");
        if (declaredValueVnd is < 0)
            throw new ArgumentOutOfRangeException(nameof(declaredValueVnd));
        if (policyVersion <= 0
            || compensationRatePercent is < 1 or > 100
            || policyCapVnd <= 0
            || noProofFallbackMultiplier <= 0)
            throw new ArgumentOutOfRangeException(nameof(policyVersion));

        return new ParcelClaim
        {
            Id = Guid.NewGuid(),
            ParcelId = parcelId,
            IncidentId = incidentId,
            OperatorId = operatorId,
            BeneficiaryUserId = beneficiaryUserId,
            Status = ParcelClaimStatus.SUBMITTED,
            DeclaredValueVnd = declaredValueVnd,
            PolicyVersion = policyVersion,
            CompensationRatePercent = compensationRatePercent,
            PolicyCapVnd = policyCapVnd,
            NoProofFallbackMultiplier = noProofFallbackMultiplier,
        };
    }

    public void BeginReview()
    {
        if (Status != ParcelClaimStatus.SUBMITTED)
            throw new InvalidOperationException("Only submitted claims can enter review.");
        Status = ParcelClaimStatus.UNDER_REVIEW;
    }

    public void Approve(
        long? provenDirectLossVnd,
        int compensationRatePercent,
        long policyCapVnd,
        long cargoAwardVnd,
        long freightRefundVnd,
        string reason,
        Guid decidedBy,
        DateTimeOffset decidedAt)
    {
        if (Status != ParcelClaimStatus.UNDER_REVIEW)
            throw new InvalidOperationException("Only claims under review can be approved.");
        ValidateAward(provenDirectLossVnd, compensationRatePercent, policyCapVnd, cargoAwardVnd, freightRefundVnd);
        Status = ParcelClaimStatus.APPROVED;
        ProvenDirectLossVnd = provenDirectLossVnd;
        CompensationRatePercent = compensationRatePercent;
        PolicyCapVnd = policyCapVnd;
        CargoAwardVnd = cargoAwardVnd;
        FreightRefundVnd = freightRefundVnd;
        TotalAwardVnd = checked(cargoAwardVnd + freightRefundVnd);
        DecisionReason = Normalize(reason) ?? throw new ArgumentException("Decision reason is required.");
        DecidedBy = decidedBy;
        DecidedAt = decidedAt;
    }

    public void Reject(string reason, Guid decidedBy, DateTimeOffset decidedAt)
    {
        if (Status != ParcelClaimStatus.UNDER_REVIEW)
            throw new InvalidOperationException("Only claims under review can be rejected.");
        Status = ParcelClaimStatus.REJECTED;
        DecisionReason = Normalize(reason) ?? throw new ArgumentException("Decision reason is required.");
        DecidedBy = decidedBy;
        DecidedAt = decidedAt;
    }

    public void MarkFundingPending()
    {
        if (Status != ParcelClaimStatus.APPROVED)
            throw new InvalidOperationException("Only approved claims can await funding.");
        Status = ParcelClaimStatus.FUNDING_PENDING;
    }

    public void MarkPaid(Guid payoutReferenceId, DateTimeOffset paidAt)
    {
        if (Status is not (ParcelClaimStatus.APPROVED or ParcelClaimStatus.FUNDING_PENDING))
            throw new InvalidOperationException("Only approved or pending-funded claims can be paid.");
        if (payoutReferenceId == Guid.Empty)
            throw new ArgumentException("Payout reference is required.", nameof(payoutReferenceId));
        Status = ParcelClaimStatus.PAID;
        PayoutReferenceId = payoutReferenceId;
        PaidAt = paidAt;
    }

    private static void ValidateAward(
        long? provenDirectLossVnd,
        int compensationRatePercent,
        long policyCapVnd,
        long cargoAwardVnd,
        long freightRefundVnd)
    {
        if (provenDirectLossVnd is < 0)
            throw new ArgumentOutOfRangeException(nameof(provenDirectLossVnd));
        if (compensationRatePercent is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(compensationRatePercent));
        if (policyCapVnd <= 0 || cargoAwardVnd < 0 || freightRefundVnd < 0)
            throw new ArgumentOutOfRangeException(nameof(policyCapVnd));
        if (cargoAwardVnd > policyCapVnd)
            throw new ArgumentException("Cargo award cannot exceed policy cap.", nameof(cargoAwardVnd));
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
