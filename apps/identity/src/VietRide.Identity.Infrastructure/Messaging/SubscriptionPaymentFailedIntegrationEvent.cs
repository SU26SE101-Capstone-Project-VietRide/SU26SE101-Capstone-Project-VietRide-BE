using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Identity.Infrastructure.Messaging;

public sealed class SubscriptionPaymentFailedIntegrationEvent : IntegrationEventBase
{
    public override string EventType => "payment.subscription.payment_failed";
    public Guid PaymentId { get; init; }
    public Guid UpgradeAttemptId { get; init; }
    public Guid OperatorId { get; init; }
    public Guid OperatorSubscriptionId { get; init; }
    public string? ResponseCode { get; init; }
}
