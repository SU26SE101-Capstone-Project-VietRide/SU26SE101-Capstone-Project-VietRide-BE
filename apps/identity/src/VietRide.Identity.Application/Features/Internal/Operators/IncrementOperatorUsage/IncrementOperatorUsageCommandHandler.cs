using MediatR;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Internal.Operators.GetInternalOperatorSubscription;
using VietRide.Identity.Application.Features.Subscriptions;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Domain.Exceptions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Application.Features.Internal.Operators.IncrementOperatorUsage;

public sealed class IncrementOperatorUsageCommandHandler
    : IRequestHandler<IncrementOperatorUsageCommand, InternalOperatorSubscriptionDto>
{
    private readonly IOperatorRepository _operators;
    private readonly IOperatorSubscriptionRepository _operatorSubscriptions;
    private readonly ISubscriptionUsageWarningPublisher _usageWarnings;
    private readonly IClock _clock;

    public IncrementOperatorUsageCommandHandler(
        IOperatorRepository operators,
        IOperatorSubscriptionRepository operatorSubscriptions,
        ISubscriptionUsageWarningPublisher usageWarnings,
        IClock? clock = null)
    {
        _operators = operators;
        _operatorSubscriptions = operatorSubscriptions;
        _usageWarnings = usageWarnings;
        _clock = clock ?? new SystemClock();
    }

    public async Task<InternalOperatorSubscriptionDto> Handle(
        IncrementOperatorUsageCommand request,
        CancellationToken cancellationToken)
    {
        if (!await _operators.ExistsAsync(request.OperatorId, cancellationToken))
            throw new NotFoundException(nameof(Operator), request.OperatorId);

        var current = await _operatorSubscriptions.GetCurrentWithPlanByOperatorIdAsync(
            request.OperatorId,
            cancellationToken)
            ?? throw new NotFoundException(nameof(OperatorSubscription), request.OperatorId);

        var decisionAt = _clock.UtcNow;
        EnsureSubscriptionCanIncrement(current.Subscription, decisionAt);

        var resource = Enum.Parse<SubscriptionUsageResource>(request.Resource, ignoreCase: false);
        var updated = await _operatorSubscriptions.TryIncrementUsageWithinLimitAsync(
            request.OperatorId,
            resource,
            request.Delta,
            decisionAt,
            cancellationToken);

        if (updated is null)
            throw new IdentityDomainException(
                "SUBSCRIPTION_LIMIT_EXCEEDED",
                "Subscription limit exceeded for the requested usage resource.");

        await _usageWarnings.EnqueueIfThresholdCrossedAsync(
            updated.Value.Subscription,
            updated.Value.Plan,
            resource,
            request.Delta,
            null,
            cancellationToken);

        return InternalOperatorSubscriptionMapper.ToDto(updated.Value.Subscription, updated.Value.Plan, decisionAt);
    }

    private static void EnsureSubscriptionCanIncrement(
        OperatorSubscription subscription,
        DateTimeOffset decisionAt)
    {
        switch (SubscriptionEffectiveState.GetStatus(subscription, decisionAt))
        {
            case SubscriptionStatus.ACTIVE:
            case SubscriptionStatus.PENDING_PAYMENT:
                return;
            case SubscriptionStatus.EXPIRED:
                throw new IdentityDomainException("SUBSCRIPTION_EXPIRED", "Operator subscription has expired.");
            default:
                throw new ValidationException(
                    "Operator subscription must be active before usage can be incremented.",
                    [new ValidationError(nameof(subscription.Status), "Operator subscription must be ACTIVE.")]);
        }
    }
}
