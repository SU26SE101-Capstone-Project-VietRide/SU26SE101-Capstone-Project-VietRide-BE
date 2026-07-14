using VietRide.Payment.Application.Models;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Application.Events;

public sealed class PaymentSucceededIntegrationEvent(
    Guid paymentId,
    PaymentReferenceType referenceType,
    Guid referenceId,
    long amount,
    PaymentMethod method,
    PaymentContextV1 context) : IntegrationEventBase
{
    public override string EventType => "payment.payment.succeeded";

    public Guid PaymentId { get; } = paymentId;
    public string ReferenceType { get; } = referenceType.ToString();
    public Guid ReferenceId { get; } = referenceId;
    public long Amount { get; } = amount;
    public string Method { get; } = method.ToString();
    public PaymentContextV1 Context { get; } = context;
}
