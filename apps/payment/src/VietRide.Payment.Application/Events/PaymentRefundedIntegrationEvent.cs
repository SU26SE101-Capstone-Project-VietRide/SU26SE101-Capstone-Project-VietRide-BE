using VietRide.Payment.Application.Models;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Application.Events;

public sealed class PaymentRefundedIntegrationEvent(
    Guid paymentId,
    PaymentReferenceType referenceType,
    Guid referenceId,
    long amount,
    PaymentContextV1 context) : IntegrationEventBase
{
    public override string EventType => "payment.payment.refunded";

    public Guid PaymentId { get; } = paymentId;
    public string ReferenceType { get; } = referenceType.ToString();
    public Guid ReferenceId { get; } = referenceId;
    public long Amount { get; } = amount;
    public PaymentContextV1 Context { get; } = context;
}
