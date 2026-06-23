using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Application.Events;

public sealed class PaymentFailedIntegrationEvent(
    Guid paymentId,
    PaymentReferenceType referenceType,
    Guid referenceId,
    string reason) : IntegrationEventBase
{
    public override string EventType => "payment.payment.failed";

    public Guid PaymentId { get; } = paymentId;
    public string ReferenceType { get; } = referenceType.ToString();
    public Guid ReferenceId { get; } = referenceId;
    public string Reason { get; } = reason;
}
