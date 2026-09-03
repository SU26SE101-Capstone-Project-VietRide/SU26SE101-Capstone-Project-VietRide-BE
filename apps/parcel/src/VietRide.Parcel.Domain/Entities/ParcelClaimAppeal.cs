using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Domain.Entities;

public sealed class ParcelClaimAppeal : BaseEntity<Guid>
{
    public Guid ClaimId { get; private set; }
    public Guid ParcelId { get; private set; }
    public Guid IncidentId { get; private set; }
    public Guid OperatorId { get; private set; }
    public Guid BeneficiaryUserId { get; private set; }
    public ParcelClaimStatus OriginalClaimStatus { get; private set; }
    public long OriginalTotalAwardVnd { get; private set; }
    public ParcelClaimAppealStatus Status { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public Guid SubmittedByUserId { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }
    public ParcelClaimProofStatus? ProofStatus { get; private set; }
    public long? RevisedProvenDirectLossVnd { get; private set; }
    public long RevisedCargoAwardVnd { get; private set; }
    public long RevisedFreightRefundVnd { get; private set; }
    public long RevisedTotalAwardVnd { get; private set; }
    public long SupplementaryAwardVnd { get; private set; }
    public string? DecisionReason { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public Guid? PayoutReferenceId { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public Guid IdempotencyKey { get; private set; }

    private ParcelClaimAppeal()
    {
    }

    public static ParcelClaimAppeal Submit(
        ParcelClaim claim,
        string reason,
        Guid submittedByUserId,
        DateTimeOffset submittedAt,
        Guid idempotencyKey)
    {
        if (claim.Status is not (ParcelClaimStatus.PAID or ParcelClaimStatus.REJECTED))
            throw new InvalidOperationException("Only paid or rejected claims can be appealed.");
        if (submittedByUserId == Guid.Empty || idempotencyKey == Guid.Empty)
            throw new ArgumentException("Appeal actor and idempotency key are required.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Appeal reason is required.", nameof(reason));

        return new ParcelClaimAppeal
        {
            Id = Guid.NewGuid(),
            ClaimId = claim.Id,
            ParcelId = claim.ParcelId,
            IncidentId = claim.IncidentId,
            OperatorId = claim.OperatorId,
            BeneficiaryUserId = claim.BeneficiaryUserId,
            OriginalClaimStatus = claim.Status,
            OriginalTotalAwardVnd = claim.Status == ParcelClaimStatus.PAID
                ? claim.TotalAwardVnd
                : 0,
            Status = ParcelClaimAppealStatus.SUBMITTED,
            Reason = reason.Trim(),
            SubmittedByUserId = submittedByUserId,
            SubmittedAt = submittedAt,
            IdempotencyKey = idempotencyKey,
        };
    }

    public void BeginReview()
    {
        if (Status != ParcelClaimAppealStatus.SUBMITTED)
            throw new InvalidOperationException("Only submitted appeals can enter review.");
        Status = ParcelClaimAppealStatus.UNDER_REVIEW;
        RowVersion++;
    }

    public void UpholdOriginalDecision(
        ParcelClaimProofStatus proofStatus,
        long? revisedProvenDirectLossVnd,
        string decisionReason,
        Guid decidedByUserId,
        DateTimeOffset decidedAt)
    {
        EnsureUnderReview();
        ValidateProof(proofStatus, revisedProvenDirectLossVnd);
        ProofStatus = proofStatus;
        RevisedProvenDirectLossVnd = revisedProvenDirectLossVnd;
        SetDecisionAudit(decisionReason, decidedByUserId, decidedAt);
        Status = ParcelClaimAppealStatus.UPHELD;
        RowVersion++;
    }

    public void ApproveAdjustment(
        ParcelClaimProofStatus proofStatus,
        long? revisedProvenDirectLossVnd,
        long revisedCargoAwardVnd,
        long revisedFreightRefundVnd,
        string decisionReason,
        Guid decidedByUserId,
        DateTimeOffset decidedAt)
    {
        EnsureUnderReview();
        ValidateProof(proofStatus, revisedProvenDirectLossVnd);
        if (revisedProvenDirectLossVnd is < 0 || revisedCargoAwardVnd < 0 || revisedFreightRefundVnd < 0)
            throw new ArgumentOutOfRangeException(nameof(revisedProvenDirectLossVnd));
        if (proofStatus != ParcelClaimProofStatus.VERIFIED && revisedCargoAwardVnd != 0)
            throw new ArgumentException("Cargo compensation requires verified proof.", nameof(revisedCargoAwardVnd));
        var revisedTotal = checked(revisedCargoAwardVnd + revisedFreightRefundVnd);
        var supplementary = checked(revisedTotal - OriginalTotalAwardVnd);
        if (supplementary <= 0)
            throw new ArgumentException("An approved appeal must increase the total award.");

        ProofStatus = proofStatus;
        RevisedProvenDirectLossVnd = revisedProvenDirectLossVnd;
        RevisedCargoAwardVnd = revisedCargoAwardVnd;
        RevisedFreightRefundVnd = revisedFreightRefundVnd;
        RevisedTotalAwardVnd = revisedTotal;
        SupplementaryAwardVnd = supplementary;
        SetDecisionAudit(decisionReason, decidedByUserId, decidedAt);
        Status = ParcelClaimAppealStatus.ADJUSTMENT_APPROVED;
        RowVersion++;
    }

    public void MarkFundingPending()
    {
        if (Status != ParcelClaimAppealStatus.ADJUSTMENT_APPROVED)
            throw new InvalidOperationException("Only an approved appeal can await funding.");
        Status = ParcelClaimAppealStatus.FUNDING_PENDING;
        RowVersion++;
    }

    public void MarkPaid(Guid payoutReferenceId, DateTimeOffset paidAt)
    {
        if (Status is not (ParcelClaimAppealStatus.ADJUSTMENT_APPROVED
            or ParcelClaimAppealStatus.FUNDING_PENDING))
            throw new InvalidOperationException("Only approved or funding-pending appeals can be paid.");
        if (payoutReferenceId == Guid.Empty)
            throw new ArgumentException("Payout reference is required.", nameof(payoutReferenceId));
        Status = ParcelClaimAppealStatus.PAID;
        PayoutReferenceId = payoutReferenceId;
        PaidAt = paidAt;
        RowVersion++;
    }

    private void EnsureUnderReview()
    {
        if (Status != ParcelClaimAppealStatus.UNDER_REVIEW)
            throw new InvalidOperationException("Only appeals under review can be decided.");
    }

    private void SetDecisionAudit(
        string decisionReason,
        Guid decidedByUserId,
        DateTimeOffset decidedAt)
    {
        if (string.IsNullOrWhiteSpace(decisionReason) || decidedByUserId == Guid.Empty)
            throw new ArgumentException("Appeal decision reason and reviewer are required.");
        DecisionReason = decisionReason.Trim();
        DecidedByUserId = decidedByUserId;
        DecidedAt = decidedAt;
    }

    private static void ValidateProof(
        ParcelClaimProofStatus proofStatus,
        long? revisedProvenDirectLossVnd)
    {
        if (!Enum.IsDefined(proofStatus))
            throw new ArgumentOutOfRangeException(nameof(proofStatus));
        if (revisedProvenDirectLossVnd is < 0)
            throw new ArgumentOutOfRangeException(nameof(revisedProvenDirectLossVnd));
        if (proofStatus == ParcelClaimProofStatus.VERIFIED && !revisedProvenDirectLossVnd.HasValue)
            throw new ArgumentException("Verified proof requires a proven direct loss.", nameof(revisedProvenDirectLossVnd));
        if (proofStatus != ParcelClaimProofStatus.VERIFIED && revisedProvenDirectLossVnd.HasValue)
            throw new ArgumentException("Unverified or missing proof cannot carry a proven direct loss.", nameof(revisedProvenDirectLossVnd));
    }
}
