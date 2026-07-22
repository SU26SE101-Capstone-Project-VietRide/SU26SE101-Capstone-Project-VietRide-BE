using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Identity.Infrastructure.Messaging;

public sealed class SubscriptionPaymentSucceededIntegrationEvent : IntegrationEventBase
{
    public override string EventType => "payment.subscription.payment_succeeded";
    public Guid PaymentId { get; init; }
    public Guid UpgradeAttemptId { get; init; }
    public Guid OperatorId { get; init; }
    public Guid OperatorSubscriptionId { get; init; }
    public Guid PlanId { get; init; }
    public long Amount { get; init; }
    public string Method { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public string BillingPeriod { get; init; } = string.Empty;
    public DateTimeOffset PeriodFrom { get; init; }
    public DateTimeOffset PeriodTo { get; init; }
    public DateTimeOffset SucceededAt { get; init; }
    public SubscriptionBuyerSnapshot BuyerSnapshot { get; init; } = new();
}
