using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Application.Events;

public sealed class BookingPaymentRefundRequestedIntegrationEvent(
    Guid paymentId,
    string paymentReferenceType,
    Guid paymentReferenceId,
    Guid bookingId,
    Guid userId,
    long amount,
    string reason) : IntegrationEventBase
{
    public const string EventTypeValue = "booking.payment_refund.requested";

    [JsonIgnore]
    public override string EventType => EventTypeValue;

    public Guid PaymentId { get; } = paymentId;
    public string PaymentReferenceType { get; } = paymentReferenceType;
    public Guid PaymentReferenceId { get; } = paymentReferenceId;
    public Guid BookingId { get; } = bookingId;
    public Guid UserId { get; } = userId;
    public long Amount { get; } = amount >= 0
        ? amount
        : throw new ArgumentOutOfRangeException(nameof(amount), "Refund amount cannot be negative.");
    public string Reason { get; } = reason;
}
