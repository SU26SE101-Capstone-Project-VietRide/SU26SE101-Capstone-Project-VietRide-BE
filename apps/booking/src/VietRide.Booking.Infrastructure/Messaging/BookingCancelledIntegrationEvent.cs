using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

public sealed record BookingCancelledIntegrationEvent : IIntegrationEvent
{
    public const string EventType = "booking.booking.cancelled";

    public Guid BookingId { get; init; }
    public Guid UserId { get; init; }
    public long RefundAmount { get; init; }
    public bool RefundOverride { get; init; }
    public string CancellationReason { get; init; } = string.Empty;

    [JsonIgnore]
    public Guid EventId => BookingId;

    [JsonIgnore]
    public DateTime OccurredAt => DateTime.UtcNow;

    [JsonIgnore]
    string IIntegrationEvent.EventType => EventType;
}
