using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Identity.Infrastructure.Messaging;

public sealed class SubscriptionPaymentSucceededIntegrationEvent : IntegrationEventBase
{
    public override string EventType => "payment.subscription.payment_succeeded";
    public Guid PaymentId { get; init; }
    public Guid UpgradeAttemptId { get; init; }
    public Guid OperatorId { get; init; }
    public long Amount { get; init; }
}
