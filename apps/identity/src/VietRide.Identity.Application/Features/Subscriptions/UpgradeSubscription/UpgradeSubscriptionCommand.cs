using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Identity.Application.Features.Subscriptions.UpgradeSubscription;

[SkipTransaction]
public sealed record UpgradeSubscriptionCommand(
    Guid OperatorId,
    Guid PlanId,
    string BillingPeriod,
    string PaymentMethod,
    string? ReturnUrl,
    string IdempotencyKey,
    string ClientIpAddress) : IRequest<SubscriptionUpgradeResponseDto>;
