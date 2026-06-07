using MediatR;
using VietRide.Identity.Application.Features.Internal.Operators.GetInternalOperatorSubscription;

namespace VietRide.Identity.Application.Features.Internal.Operators.IncrementOperatorUsage;

public sealed record IncrementOperatorUsageCommand(
    Guid OperatorId,
    string Resource,
    int Delta) : IRequest<InternalOperatorSubscriptionDto>;
