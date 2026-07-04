using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Parcel.Infrastructure.Messaging;

public sealed record TripStartedIntegrationEvent : IIntegrationEvent
{
    public const string EventType = "trip.trip.started";

    public Guid TripId { get; init; }

    [JsonIgnore]
    public Guid EventId => TripId;

    [JsonIgnore]
    public DateTime OccurredAt => DateTime.UtcNow;

    [JsonIgnore]
    string IIntegrationEvent.EventType => EventType;
}
