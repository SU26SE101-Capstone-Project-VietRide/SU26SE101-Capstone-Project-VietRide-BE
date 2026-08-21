using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Domain.Entities;

namespace VietRide.Parcel.Application.Features.Reliability.Policies;

public sealed class GetParcelCompensationPolicyQueryHandler
    : IRequestHandler<GetParcelCompensationPolicyQuery, ParcelCompensationPolicyResponse>
{
    private readonly IParcelReliabilityRepository _reliability;

    public GetParcelCompensationPolicyQueryHandler(IParcelReliabilityRepository reliability)
    {
        _reliability = reliability;
    }

    public async Task<ParcelCompensationPolicyResponse> Handle(
        GetParcelCompensationPolicyQuery request,
        CancellationToken cancellationToken)
    {
        var policy = await _reliability.GetCompensationPolicyAsync(request.OperatorId, cancellationToken);
        return policy is null
            ? new ParcelCompensationPolicyResponse(
                request.OperatorId,
                ParcelCompensationPolicy.DefaultRatePercent,
                ParcelCompensationPolicy.DefaultMaximumCompensationVnd,
                ParcelCompensationPolicy.DefaultNoProofFallbackMultiplier,
                ParcelCompensationPolicy.DefaultClaimWindowDays,
                ParcelCompensationPolicy.DefaultSearchSlaHours,
                ParcelCompensationPolicy.DefaultDecisionSlaBusinessDays,
                ParcelCompensationPolicy.DefaultPayoutSlaBusinessDays,
                1,
                false,
                Defaults(),
                false,
                true,
                null,
                null)
            : new ParcelCompensationPolicyResponse(
                policy.OperatorId,
                policy.CompensationRatePercent,
                policy.MaxCompensationVnd,
                policy.NoProofFallbackMultiplier,
                policy.ClaimWindowDays,
                policy.SearchSlaHours,
                policy.DecisionSlaBusinessDays,
                policy.PayoutSlaBusinessDays,
                policy.Version,
                policy.BelowDefaultAcknowledged,
                Defaults(),
                policy.CompensationRatePercent < ParcelCompensationPolicy.DefaultRatePercent
                    || policy.MaxCompensationVnd < ParcelCompensationPolicy.DefaultMaximumCompensationVnd,
                true,
                policy.UpdatedAt,
                policy.UpdatedByUserId);
    }

    internal static ParcelCompensationPolicyDefaultsResponse Defaults()
        => new(
            ParcelCompensationPolicy.DefaultRatePercent,
            ParcelCompensationPolicy.DefaultMaximumCompensationVnd,
            ParcelCompensationPolicy.DefaultNoProofFallbackMultiplier,
            ParcelCompensationPolicy.DefaultClaimWindowDays,
            ParcelCompensationPolicy.DefaultSearchSlaHours,
            ParcelCompensationPolicy.DefaultDecisionSlaBusinessDays,
            ParcelCompensationPolicy.DefaultPayoutSlaBusinessDays);
}
