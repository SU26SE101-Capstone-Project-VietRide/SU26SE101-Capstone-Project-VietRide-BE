using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Application.Features.Subscriptions.GetOperatorSubscription;

public sealed class GetOperatorSubscriptionQueryHandler
    : IRequestHandler<GetOperatorSubscriptionQuery, OperatorSubscriptionDto>
{
    private readonly IOperatorSubscriptionRepository _subscriptions;
    private readonly ISubscriptionUpgradeAttemptRepository _attempts;
    private readonly ISubscriptionPlanRepository _plans;
    private readonly IClock _clock;

    public GetOperatorSubscriptionQueryHandler(
        IOperatorSubscriptionRepository subscriptions,
        ISubscriptionUpgradeAttemptRepository attempts,
        ISubscriptionPlanRepository plans,
        IClock clock)
    {
        _subscriptions = subscriptions;
        _attempts = attempts;
        _plans = plans;
        _clock = clock;
    }

    public async Task<OperatorSubscriptionDto> Handle(
        GetOperatorSubscriptionQuery request,
        CancellationToken cancellationToken)
    {
        var current = await _subscriptions.GetCurrentWithPlanByOperatorIdAsync(request.OperatorId, cancellationToken)
            ?? throw new NotFoundException(nameof(OperatorSubscription), request.OperatorId);
        var pending = await _attempts.GetActiveBySubscriptionIdAsync(current.Subscription.Id, cancellationToken);
        var targetPlan = pending is null
            ? null
            : await _plans.GetByIdAsync(pending.TargetPlanId, cancellationToken)
                ?? throw new NotFoundException(nameof(SubscriptionPlan), pending.TargetPlanId);
        return SubscriptionMapper.ToSubscriptionDto(current.Subscription, current.Plan, pending, targetPlan, _clock.UtcNow);
    }
}
