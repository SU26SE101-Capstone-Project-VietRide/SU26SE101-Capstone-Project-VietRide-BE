using MediatR;

namespace VietRide.Identity.Application.Features.Subscriptions.GetOperatorSubscription;

public sealed record GetOperatorSubscriptionQuery(Guid OperatorId) : IRequest<OperatorSubscriptionDto>;
