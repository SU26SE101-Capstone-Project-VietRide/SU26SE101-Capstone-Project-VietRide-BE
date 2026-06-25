using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Application.Events;

public sealed class PaymentExpiredIntegrationEvent(
    Guid paymentId,
    PaymentReferenceType referenceType,
    Guid referenceId) : IntegrationEventBase
{
    public override string EventType => "payment.payment.expired";

    public Guid PaymentId { get; } = paymentId;
    public string ReferenceType { get; } = referenceType.ToString();
    public Guid ReferenceId { get; } = referenceId;
}
