using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Policies;

public sealed record UpdateParcelCompensationPolicyCommand(
    Guid OperatorId,
    Guid UpdatedByUserId,
    int CompensationRatePercent,
    long MaxCompensationVnd,
    int NoProofFallbackMultiplier,
    int ClaimWindowDays,
    int SearchSlaHours,
    int DecisionSlaBusinessDays,
    int PayoutSlaBusinessDays,
    bool BelowDefaultAcknowledged) : IRequest<ParcelCompensationPolicyResponse>;

public sealed record ParcelCompensationPolicyResponse(
    Guid OperatorId,
    int CompensationRatePercent,
    long MaxCompensationVnd,
    int NoProofFallbackMultiplier,
    int ClaimWindowDays,
    int SearchSlaHours,
    int DecisionSlaBusinessDays,
    int PayoutSlaBusinessDays,
    int Version,
    bool BelowDefaultAcknowledged,
    ParcelCompensationPolicyDefaultsResponse? PlatformDefaultPolicy = null,
    bool IsBelowPlatformDefault = false,
    bool EffectiveForNewParcelsOnly = true,
    DateTimeOffset? UpdatedAt = null,
    Guid? UpdatedBy = null);

public sealed record ParcelCompensationPolicyDefaultsResponse(
    int CompensationRatePercent,
    long MaxCompensationVnd,
    int NoProofFallbackMultiplier,
    int ClaimWindowDays,
    int SearchSlaHours,
    int DecisionSlaBusinessDays,
    int PayoutSlaBusinessDays);
