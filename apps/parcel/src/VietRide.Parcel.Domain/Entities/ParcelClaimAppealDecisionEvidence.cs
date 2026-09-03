using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Domain.Entities;

public sealed class ParcelClaimAppealDecisionEvidence : BaseEntity<Guid>
{
    public Guid AppealId { get; private set; }
    public Guid ClaimId { get; private set; }
    public Guid EvidenceId { get; private set; }
    public Guid AcceptedByUserId { get; private set; }
    public DateTimeOffset AcceptedAt { get; private set; }

    private ParcelClaimAppealDecisionEvidence()
    {
    }

    public static ParcelClaimAppealDecisionEvidence Create(
        Guid appealId,
        Guid claimId,
        Guid evidenceId,
        Guid acceptedByUserId,
        DateTimeOffset acceptedAt)
    {
        if (appealId == Guid.Empty
            || claimId == Guid.Empty
            || evidenceId == Guid.Empty
            || acceptedByUserId == Guid.Empty)
        {
            throw new ArgumentException("Appeal, claim, evidence and reviewer ids are required.");
        }

        return new ParcelClaimAppealDecisionEvidence
        {
            Id = Guid.NewGuid(),
            AppealId = appealId,
            ClaimId = claimId,
            EvidenceId = evidenceId,
            AcceptedByUserId = acceptedByUserId,
            AcceptedAt = acceptedAt,
        };
    }
}
