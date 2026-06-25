using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Application.Events;

public sealed class PaymentSucceededIntegrationEvent(
    Guid paymentId,
    PaymentReferenceType referenceType,
    Guid referenceId,
    long amount) : IntegrationEventBase
{
    public override string EventType => "payment.payment.succeeded";

    public Guid PaymentId { get; } = paymentId;
    public string ReferenceType { get; } = referenceType.ToString();
    public Guid ReferenceId { get; } = referenceId;
    public long Amount { get; } = amount;
}
