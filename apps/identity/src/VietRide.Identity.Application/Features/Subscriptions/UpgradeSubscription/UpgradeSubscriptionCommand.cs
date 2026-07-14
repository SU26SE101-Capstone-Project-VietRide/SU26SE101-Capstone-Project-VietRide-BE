using MediatR;

namespace VietRide.Identity.Application.Features.Subscriptions.UpgradeSubscription;

public sealed record UpgradeSubscriptionCommand(
    Guid OperatorId,
    Guid PlanId,
    string BillingPeriod,
    string IdempotencyKey,
    string ClientIpAddress) : IRequest<SubscriptionUpgradeResponseDto>;
