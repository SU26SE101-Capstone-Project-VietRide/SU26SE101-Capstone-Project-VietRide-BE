using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Application.Events;

public sealed class SubscriptionPaymentFailedIntegrationEvent(
    Guid paymentId,
    Guid upgradeAttemptId,
    Guid operatorId,
    Guid operatorSubscriptionId,
    string? responseCode) : IntegrationEventBase
{
    public override string EventType => "payment.subscription.payment_failed";
    public Guid PaymentId { get; } = paymentId;
    public Guid UpgradeAttemptId { get; } = upgradeAttemptId;
    public Guid OperatorId { get; } = operatorId;
    public Guid OperatorSubscriptionId { get; } = operatorSubscriptionId;
    public string? ResponseCode { get; } = responseCode;
}
