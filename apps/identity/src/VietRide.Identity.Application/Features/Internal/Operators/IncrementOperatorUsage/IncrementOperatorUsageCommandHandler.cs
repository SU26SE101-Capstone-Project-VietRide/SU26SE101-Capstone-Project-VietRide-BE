using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Internal.Operators.GetInternalOperatorSubscription;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Domain.Exceptions;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.Application.Features.Internal.Operators.IncrementOperatorUsage;

public sealed class IncrementOperatorUsageCommandHandler
    : IRequestHandler<IncrementOperatorUsageCommand, InternalOperatorSubscriptionDto>
{
    private readonly IOperatorRepository _operators;
    private readonly IOperatorSubscriptionRepository _operatorSubscriptions;

    public IncrementOperatorUsageCommandHandler(
        IOperatorRepository operators,
        IOperatorSubscriptionRepository operatorSubscriptions)
    {
        _operators = operators;
        _operatorSubscriptions = operatorSubscriptions;
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

        EnsureSubscriptionCanIncrement(current.Subscription);

        var resource = Enum.Parse<SubscriptionUsageResource>(request.Resource, ignoreCase: false);
        var updated = await _operatorSubscriptions.TryIncrementUsageWithinLimitAsync(
            request.OperatorId,
            resource,
            request.Delta,
            cancellationToken);

        if (updated is null)
            throw new IdentityDomainException(
                "SUBSCRIPTION_LIMIT_EXCEEDED",
                "Subscription limit exceeded for the requested usage resource.");

        return InternalOperatorSubscriptionMapper.ToDto(updated.Value.Subscription, updated.Value.Plan);
    }

    private static void EnsureSubscriptionCanIncrement(OperatorSubscription subscription)
    {
        switch (subscription.Status)
        {
            case SubscriptionStatus.ACTIVE:
                return;
            case SubscriptionStatus.EXPIRED:
                throw new IdentityDomainException("SUBSCRIPTION_EXPIRED", "Operator subscription has expired.");
            case SubscriptionStatus.PENDING_PAYMENT:
                throw new ConflictException("SUBSCRIPTION_PAYMENT_PENDING", "Operator subscription payment is pending.");
            default:
                throw new ValidationException(
                    "Operator subscription must be active before usage can be incremented.",
                    [new ValidationError(nameof(subscription.Status), "Operator subscription must be ACTIVE.")]);
        }
    }
}
