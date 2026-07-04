using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Parcel.Infrastructure.Messaging;

public sealed record TripCancelledIntegrationEvent : IIntegrationEvent
{
    public const string EventType = "trip.trip.cancelled";

    public Guid TripId { get; init; }

    public Guid EventId { get; init; }
    public DateTime OccurredAt { get; init; }

    [JsonIgnore]
    string IIntegrationEvent.EventType => EventType;
}
