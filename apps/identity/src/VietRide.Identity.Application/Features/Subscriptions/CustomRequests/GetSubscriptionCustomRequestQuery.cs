using VietRide.Shared.Application.Cqrs;

namespace VietRide.Identity.Application.Features.Subscriptions.CustomRequests;

public sealed record GetSubscriptionCustomRequestQuery(
    Guid RequestId,
    Guid? OperatorId) : IQuery<SubscriptionCustomRequestDto>;
