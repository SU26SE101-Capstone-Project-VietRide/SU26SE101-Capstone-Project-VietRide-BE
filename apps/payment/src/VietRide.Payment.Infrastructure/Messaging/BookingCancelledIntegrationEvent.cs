using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Payment.Infrastructure.Messaging;

/// <summary>
/// Consumer-side mirror of Booking's booking.booking.cancelled payload.
/// </summary>
public sealed record BookingCancelledIntegrationEvent(
    [property: JsonPropertyName("bookingId")] Guid BookingId,
    [property: JsonPropertyName("userId")] Guid UserId,
    [property: JsonPropertyName("refundAmount")] long RefundAmount,
    [property: JsonPropertyName("refundOverride")] bool RefundOverride,
    [property: JsonPropertyName("cancellationReason")] string? CancellationReason) : IIntegrationEvent
{
    public const string EventType = "booking.booking.cancelled";

    [JsonIgnore]
    public Guid EventId => BookingId;

    [JsonIgnore]
    public DateTime OccurredAt => DateTime.UtcNow;

    [JsonIgnore]
    string IIntegrationEvent.EventType => EventType;
}
