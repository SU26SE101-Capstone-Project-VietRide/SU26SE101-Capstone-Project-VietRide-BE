using VietRide.Shared.Application.Cqrs;

namespace VietRide.Identity.Application.Features.Subscriptions.CustomRequests;

public sealed record ListSubscriptionCustomRequestsQuery(
    Guid? OperatorId,
    string? Status = null) : IQuery<IReadOnlyList<SubscriptionCustomRequestDto>>;
