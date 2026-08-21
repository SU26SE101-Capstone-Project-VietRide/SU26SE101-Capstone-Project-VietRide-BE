namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record UpdateParcelCompensationPolicyRequest(
    int CompensationRatePercent,
    long MaxCompensationVnd,
    int NoProofFallbackMultiplier = 4,
    int ClaimWindowDays = 30,
    int SearchSlaHours = 72,
    int DecisionSlaBusinessDays = 7,
    int PayoutSlaBusinessDays = 3,
    bool BelowDefaultAcknowledged = false);
