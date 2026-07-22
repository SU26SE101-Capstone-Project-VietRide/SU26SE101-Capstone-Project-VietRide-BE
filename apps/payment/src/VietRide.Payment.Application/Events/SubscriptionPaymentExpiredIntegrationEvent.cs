using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Application.Events;

public sealed class SubscriptionPaymentExpiredIntegrationEvent(
    Guid paymentId,
    Guid upgradeAttemptId,
    Guid operatorId,
    Guid operatorSubscriptionId) : IntegrationEventBase
{
    public override string EventType => "payment.subscription.payment_expired";
    public Guid PaymentId { get; } = paymentId;
    public Guid UpgradeAttemptId { get; } = upgradeAttemptId;
    public Guid OperatorId { get; } = operatorId;
    public Guid OperatorSubscriptionId { get; } = operatorSubscriptionId;
}
