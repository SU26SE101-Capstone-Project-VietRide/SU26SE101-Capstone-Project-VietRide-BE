using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Domain.Entities;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Reliability.Policies;

public sealed class UpdateParcelCompensationPolicyCommandHandler
    : IRequestHandler<UpdateParcelCompensationPolicyCommand, ParcelCompensationPolicyResponse>
{
    private readonly IParcelReliabilityRepository _reliability;
    private readonly IClock _clock;

    public UpdateParcelCompensationPolicyCommandHandler(
        IParcelReliabilityRepository reliability,
        IClock clock)
    {
        _reliability = reliability;
        _clock = clock;
    }

    public async Task<ParcelCompensationPolicyResponse> Handle(
        UpdateParcelCompensationPolicyCommand command,
        CancellationToken cancellationToken)
    {
        var policy = await _reliability.GetCompensationPolicyAsync(command.OperatorId, cancellationToken);
        try
        {
            if (policy is null)
            {
                policy = ParcelCompensationPolicy.Create(
                    command.OperatorId,
                    command.CompensationRatePercent,
                    command.MaxCompensationVnd,
                    command.NoProofFallbackMultiplier,
                    command.ClaimWindowDays,
                    command.SearchSlaHours,
                    command.DecisionSlaBusinessDays,
                    command.PayoutSlaBusinessDays,
                    command.BelowDefaultAcknowledged,
                    command.UpdatedByUserId);
                await _reliability.AddCompensationPolicyAsync(policy, cancellationToken);
            }
            else
            {
                policy.Update(
                    command.CompensationRatePercent,
                    command.MaxCompensationVnd,
                    command.NoProofFallbackMultiplier,
                    command.ClaimWindowDays,
                    command.SearchSlaHours,
                    command.DecisionSlaBusinessDays,
                    command.PayoutSlaBusinessDays,
                    command.BelowDefaultAcknowledged,
                    command.UpdatedByUserId);
                await _reliability.UpdateCompensationPolicyAsync(policy, cancellationToken);
            }
        }
        catch (ArgumentException exception)
        {
            throw new CodedValidationException("VALIDATION_ERROR", exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            throw new CodedValidationException("POLICY_BELOW_DEFAULT_ACK_REQUIRED", exception.Message);
        }

        policy.UpdatedAt = _clock.UtcNow;

        return new ParcelCompensationPolicyResponse(
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
            GetParcelCompensationPolicyQueryHandler.Defaults(),
            policy.CompensationRatePercent < ParcelCompensationPolicy.DefaultRatePercent
                || policy.MaxCompensationVnd < ParcelCompensationPolicy.DefaultMaximumCompensationVnd,
            true,
            policy.UpdatedAt,
            policy.UpdatedByUserId);
    }
}
