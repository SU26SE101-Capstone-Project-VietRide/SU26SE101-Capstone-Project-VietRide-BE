using VietRide.Shared.Application.Cqrs;

namespace VietRide.Identity.Application.Features.Subscriptions.CustomRequests;

public sealed record GetAdminSubscriptionCustomRequestQuery(Guid RequestId)
    : IQuery<AdminSubscriptionCustomRequestDto>;
