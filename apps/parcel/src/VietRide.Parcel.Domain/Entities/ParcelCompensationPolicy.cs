using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Domain.Entities;

public sealed class ParcelCompensationPolicy : BaseEntity<Guid>
{
    public const int DefaultRatePercent = 50;
    public const long DefaultMaximumCompensationVnd = 30_000_000;
    public const int DefaultNoProofFallbackMultiplier = 2;
    public const int DefaultClaimWindowDays = 30;
    public const int DefaultSearchSlaHours = 72;
    public const int DefaultDecisionSlaBusinessDays = 7;
    public const int DefaultPayoutSlaBusinessDays = 3;

    public Guid OperatorId { get; private set; }
    public int CompensationRatePercent { get; private set; }
    public long MaxCompensationVnd { get; private set; }
    public int NoProofFallbackMultiplier { get; private set; }
    public int ClaimWindowDays { get; private set; }
    public int SearchSlaHours { get; private set; }
    public int DecisionSlaBusinessDays { get; private set; }
    public int PayoutSlaBusinessDays { get; private set; }
    public int Version { get; private set; }
    public bool BelowDefaultAcknowledged { get; private set; }
    public Guid UpdatedByUserId { get; private set; }

    private ParcelCompensationPolicy()
    {
    }

    public static ParcelCompensationPolicy CreateDefault(Guid operatorId, Guid updatedByUserId)
        => Create(
            operatorId,
            DefaultRatePercent,
            DefaultMaximumCompensationVnd,
            DefaultNoProofFallbackMultiplier,
            DefaultClaimWindowDays,
            DefaultSearchSlaHours,
            DefaultDecisionSlaBusinessDays,
            DefaultPayoutSlaBusinessDays,
            belowDefaultAcknowledged: false,
            updatedByUserId);

    public static ParcelCompensationPolicy Create(
        Guid operatorId,
        int compensationRatePercent,
        long maxCompensationVnd,
        int noProofFallbackMultiplier,
        int claimWindowDays,
        int searchSlaHours,
        int decisionSlaBusinessDays,
        int payoutSlaBusinessDays,
        bool belowDefaultAcknowledged,
        Guid updatedByUserId)
    {
        var policy = new ParcelCompensationPolicy
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorId,
            Version = 0,
        };
        policy.Update(
            compensationRatePercent,
            maxCompensationVnd,
            noProofFallbackMultiplier,
            claimWindowDays,
            searchSlaHours,
            decisionSlaBusinessDays,
            payoutSlaBusinessDays,
            belowDefaultAcknowledged,
            updatedByUserId);
        return policy;
    }

    public void Update(
        int compensationRatePercent,
        long maxCompensationVnd,
        int noProofFallbackMultiplier,
        int claimWindowDays,
        int searchSlaHours,
        int decisionSlaBusinessDays,
        int payoutSlaBusinessDays,
        bool belowDefaultAcknowledged,
        Guid updatedByUserId)
    {
        if (OperatorId == Guid.Empty || updatedByUserId == Guid.Empty)
            throw new ArgumentException("Operator and updater ids are required.");
        if (compensationRatePercent is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(compensationRatePercent));
        if (maxCompensationVnd <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxCompensationVnd));
        if (noProofFallbackMultiplier is < 1 or > DefaultNoProofFallbackMultiplier)
            throw new ArgumentOutOfRangeException(nameof(noProofFallbackMultiplier));
        if (claimWindowDays is < 1 or > 365
            || searchSlaHours is < 1 or > 720
            || decisionSlaBusinessDays is < 1 or > 90
            || payoutSlaBusinessDays is < 1 or > 90)
            throw new ArgumentOutOfRangeException(nameof(claimWindowDays));
        if ((compensationRatePercent < DefaultRatePercent
                || maxCompensationVnd < DefaultMaximumCompensationVnd)
            && !belowDefaultAcknowledged)
            throw new InvalidOperationException("A below-default policy requires explicit acknowledgement.");

        CompensationRatePercent = compensationRatePercent;
        MaxCompensationVnd = maxCompensationVnd;
        NoProofFallbackMultiplier = noProofFallbackMultiplier;
        ClaimWindowDays = claimWindowDays;
        SearchSlaHours = searchSlaHours;
        DecisionSlaBusinessDays = decisionSlaBusinessDays;
        PayoutSlaBusinessDays = payoutSlaBusinessDays;
        BelowDefaultAcknowledged = belowDefaultAcknowledged;
        UpdatedByUserId = updatedByUserId;
        Version = checked(Version + 1);
    }
}
