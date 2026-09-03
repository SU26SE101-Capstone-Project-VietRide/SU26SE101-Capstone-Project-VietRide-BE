using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Domain.Entities;

public sealed class ParcelClaimDecisionEvidence : BaseEntity<Guid>
{
    public Guid ClaimId { get; private set; }
    public Guid EvidenceId { get; private set; }
    public Guid AcceptedByUserId { get; private set; }
    public DateTimeOffset AcceptedAt { get; private set; }

    private ParcelClaimDecisionEvidence()
    {
    }

    public static ParcelClaimDecisionEvidence Create(
        Guid claimId,
        Guid evidenceId,
        Guid acceptedByUserId,
        DateTimeOffset acceptedAt)
    {
        if (claimId == Guid.Empty || evidenceId == Guid.Empty || acceptedByUserId == Guid.Empty)
            throw new ArgumentException("Claim, evidence and reviewer ids are required.");

        return new ParcelClaimDecisionEvidence
        {
            Id = Guid.NewGuid(),
            ClaimId = claimId,
            EvidenceId = evidenceId,
            AcceptedByUserId = acceptedByUserId,
            AcceptedAt = acceptedAt,
        };
    }
}
