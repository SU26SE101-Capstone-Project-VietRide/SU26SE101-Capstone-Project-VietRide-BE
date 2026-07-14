using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Trip.Infrastructure.Messaging;

public sealed record BookingShuttleCancelledIntegrationEvent : IIntegrationEvent
{
    public const string EventType = "booking.booking.cancelled";
    public Guid BookingId { get; init; }
    public Guid UserId { get; init; }

    [JsonIgnore]
    public Guid EventId => BookingId;

    [JsonIgnore]
    public DateTime OccurredAt => DateTime.UtcNow;

    [JsonIgnore]
    string IIntegrationEvent.EventType => EventType;
}
