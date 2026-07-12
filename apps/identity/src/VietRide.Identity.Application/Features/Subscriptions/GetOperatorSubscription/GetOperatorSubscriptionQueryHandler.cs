using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.Application.Features.Subscriptions.GetOperatorSubscription;

public sealed class GetOperatorSubscriptionQueryHandler
    : IRequestHandler<GetOperatorSubscriptionQuery, OperatorSubscriptionDto>
{
    private readonly IOperatorSubscriptionRepository _subscriptions;
    private readonly ISubscriptionUpgradeAttemptRepository _attempts;

    public GetOperatorSubscriptionQueryHandler(
        IOperatorSubscriptionRepository subscriptions,
        ISubscriptionUpgradeAttemptRepository attempts)
    {
        _subscriptions = subscriptions;
        _attempts = attempts;
    }

    public async Task<OperatorSubscriptionDto> Handle(
        GetOperatorSubscriptionQuery request,
        CancellationToken cancellationToken)
    {
        var current = await _subscriptions.GetCurrentWithPlanByOperatorIdAsync(request.OperatorId, cancellationToken)
            ?? throw new NotFoundException(nameof(OperatorSubscription), request.OperatorId);
        var pending = await _attempts.GetPendingBySubscriptionIdAsync(current.Subscription.Id, cancellationToken);
        return SubscriptionMapper.ToSubscriptionDto(current.Subscription, current.Plan, pending);
    }
}
