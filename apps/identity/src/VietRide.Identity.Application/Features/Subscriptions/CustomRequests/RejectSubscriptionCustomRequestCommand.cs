using MediatR;

namespace VietRide.Identity.Application.Features.Subscriptions.CustomRequests;

public sealed record RejectSubscriptionCustomRequestCommand(
    Guid CallerUserId,
    Guid RequestId,
    string Reason) : IRequest<SubscriptionCustomRequestDto>;
