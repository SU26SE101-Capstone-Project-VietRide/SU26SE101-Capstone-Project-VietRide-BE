using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.Application.Features.Internal.Operators.GetInternalOperatorSubscription;

public sealed class GetInternalOperatorSubscriptionQueryHandler
    : IRequestHandler<GetInternalOperatorSubscriptionQuery, InternalOperatorSubscriptionDto>
{
    private readonly IOperatorRepository _operators;
    private readonly IOperatorSubscriptionRepository _operatorSubscriptions;
    private readonly IClock _clock;

    public GetInternalOperatorSubscriptionQueryHandler(
        IOperatorRepository operators,
        IOperatorSubscriptionRepository operatorSubscriptions,
        IClock? clock = null)
    {
        _operators = operators;
        _operatorSubscriptions = operatorSubscriptions;
        _clock = clock ?? new SystemClock();
    }

    public async Task<InternalOperatorSubscriptionDto> Handle(
        GetInternalOperatorSubscriptionQuery request,
        CancellationToken cancellationToken)
    {
        if (!await _operators.ExistsAsync(request.OperatorId, cancellationToken))
            throw new NotFoundException(nameof(Operator), request.OperatorId);

        var subscription = await _operatorSubscriptions.GetCurrentWithPlanByOperatorIdAsync(
            request.OperatorId,
            cancellationToken)
            ?? throw new NotFoundException(nameof(OperatorSubscription), request.OperatorId);

        var decisionAt = _clock.UtcNow;
        return InternalOperatorSubscriptionMapper.ToDto(subscription.Subscription, subscription.Plan, decisionAt);
    }
}
