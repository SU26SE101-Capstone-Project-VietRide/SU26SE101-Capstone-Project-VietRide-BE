using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.Application.Features.Internal.Operators.GetInternalOperatorSubscription;

public sealed class GetInternalOperatorSubscriptionQueryHandler
    : IRequestHandler<GetInternalOperatorSubscriptionQuery, InternalOperatorSubscriptionDto>
{
    private readonly IOperatorRepository _operators;
    private readonly IOperatorSubscriptionRepository _operatorSubscriptions;

    public GetInternalOperatorSubscriptionQueryHandler(
        IOperatorRepository operators,
        IOperatorSubscriptionRepository operatorSubscriptions)
    {
        _operators = operators;
        _operatorSubscriptions = operatorSubscriptions;
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

        return InternalOperatorSubscriptionMapper.ToDto(subscription.Subscription, subscription.Plan);
    }
}
