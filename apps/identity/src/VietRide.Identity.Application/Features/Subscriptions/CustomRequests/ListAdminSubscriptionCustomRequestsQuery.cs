using VietRide.Shared.Application.Cqrs;

namespace VietRide.Identity.Application.Features.Subscriptions.CustomRequests;

public sealed record ListAdminSubscriptionCustomRequestsQuery(string? Status = null)
    : IQuery<IReadOnlyList<AdminSubscriptionCustomRequestDto>>;
