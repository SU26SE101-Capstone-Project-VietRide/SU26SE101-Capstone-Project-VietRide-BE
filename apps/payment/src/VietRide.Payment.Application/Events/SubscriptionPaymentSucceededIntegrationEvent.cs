using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Application.Events;

public sealed class SubscriptionPaymentSucceededIntegrationEvent(
    Guid paymentId,
    Guid upgradeAttemptId,
    Guid operatorId,
    long amount) : IntegrationEventBase
{
    public override string EventType => "payment.subscription.payment_succeeded";

    public Guid PaymentId { get; } = paymentId;
    public Guid UpgradeAttemptId { get; } = upgradeAttemptId;
    public Guid OperatorId { get; } = operatorId;
    public long Amount { get; } = amount;
}
