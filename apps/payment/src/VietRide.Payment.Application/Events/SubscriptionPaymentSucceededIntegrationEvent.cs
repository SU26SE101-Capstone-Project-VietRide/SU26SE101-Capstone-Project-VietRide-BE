using VietRide.Payment.Application.Models;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Application.Events;

public sealed class SubscriptionPaymentSucceededIntegrationEvent(
    Guid paymentId,
    Guid upgradeAttemptId,
    Guid operatorId,
    Guid operatorSubscriptionId,
    long amount,
    string method,
    SubscriptionPaymentContextV1 context) : IntegrationEventBase
{
    public override string EventType => "payment.subscription.payment_succeeded";

    public Guid PaymentId { get; } = paymentId;
    public Guid UpgradeAttemptId { get; } = upgradeAttemptId;
    public Guid OperatorId { get; } = operatorId;
    public Guid OperatorSubscriptionId { get; } = operatorSubscriptionId;
    public long Amount { get; } = amount;
    public string Method { get; } = method;
    public string PlanName { get; } = context.PlanName;
    public string BillingPeriod { get; } = context.BillingPeriod;
    public DateTimeOffset PeriodFrom { get; } = context.PeriodFrom;
    public DateTimeOffset PeriodTo { get; } = context.PeriodTo;
    public SubscriptionBuyerSnapshotV1 BuyerSnapshot { get; } = context.BuyerSnapshot;
}
