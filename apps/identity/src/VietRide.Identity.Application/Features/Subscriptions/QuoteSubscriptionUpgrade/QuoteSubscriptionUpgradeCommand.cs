using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Identity.Application.Features.Subscriptions.QuoteSubscriptionUpgrade;

[SkipTransaction]
public sealed record QuoteSubscriptionUpgradeCommand(
    Guid OperatorId,
    Guid PlanId,
    string BillingPeriod,
    string PaymentMethod,
    string IdempotencyKey) : IRequest<SubscriptionUpgradeQuoteDto>;
