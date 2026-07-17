using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

public sealed record StationMergedIntegrationEvent : IIntegrationEvent
{
    public const string EventType = "trip.station.merged";

    public Guid EventId { get; init; }
    public DateTime OccurredAt { get; init; }
    public Guid PrimaryStationId { get; init; }
    public Guid DuplicateStationId { get; init; }

    [JsonIgnore]
    string IIntegrationEvent.EventType => EventType;
}
