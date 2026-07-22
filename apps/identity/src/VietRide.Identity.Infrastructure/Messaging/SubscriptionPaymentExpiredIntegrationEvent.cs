using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Identity.Infrastructure.Messaging;

public sealed class SubscriptionPaymentExpiredIntegrationEvent : IntegrationEventBase
{
    public override string EventType => "payment.subscription.payment_expired";
    public Guid PaymentId { get; init; }
    public Guid UpgradeAttemptId { get; init; }
    public Guid OperatorId { get; init; }
    public Guid OperatorSubscriptionId { get; init; }
}
