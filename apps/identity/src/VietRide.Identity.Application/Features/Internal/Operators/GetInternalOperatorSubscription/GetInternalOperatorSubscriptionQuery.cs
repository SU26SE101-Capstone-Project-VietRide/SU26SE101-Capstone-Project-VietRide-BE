using MediatR;

namespace VietRide.Identity.Application.Features.Internal.Operators.GetInternalOperatorSubscription;

public sealed record GetInternalOperatorSubscriptionQuery(Guid OperatorId) : IRequest<InternalOperatorSubscriptionDto>;
