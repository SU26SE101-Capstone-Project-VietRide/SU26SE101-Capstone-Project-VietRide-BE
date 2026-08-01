using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Infrastructure.Messaging;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BookingPaymentRefundRequestedIntegrationEvent : IIntegrationEvent
{
    public const string EventType = "booking.payment_refund.requested";

    [JsonPropertyName("eventId"), JsonRequired]
    public Guid EventId { get; init; }

    [JsonPropertyName("occurredAt"), JsonRequired]
    public DateTimeOffset OccurredAtOffset { get; init; }

    [JsonPropertyName("paymentId"), JsonRequired]
    public Guid PaymentId { get; init; }

    [JsonPropertyName("paymentReferenceType"), JsonRequired]
    public string PaymentReferenceType { get; init; } = string.Empty;

    [JsonPropertyName("paymentReferenceId"), JsonRequired]
    public Guid PaymentReferenceId { get; init; }

    [JsonPropertyName("bookingId"), JsonRequired]
    public Guid BookingId { get; init; }

    [JsonPropertyName("userId"), JsonRequired]
    public Guid UserId { get; init; }

    [JsonPropertyName("amount"), JsonRequired]
    public long Amount { get; init; }

    [JsonPropertyName("reason"), JsonRequired]
    public string Reason { get; init; } = string.Empty;

    DateTime IIntegrationEvent.OccurredAt => OccurredAtOffset.UtcDateTime;

    string IIntegrationEvent.EventType => EventType;

    public void Validate()
    {
        if (EventId == Guid.Empty
            || PaymentId == Guid.Empty
            || PaymentReferenceId == Guid.Empty
            || BookingId == Guid.Empty
            || UserId == Guid.Empty
            || Amount < 0
            || PaymentReferenceType is not ("BOOKING" or "BOOKING_GROUP")
            || Reason is not ("PAYMENT_CAPTURE_AFTER_BOOKING_EXPIRY" or "SEAT_CONFIRMATION_FAILED"))
        {
            throw new ArgumentException("Booking payment-refund request event is malformed.");
        }
    }
}
