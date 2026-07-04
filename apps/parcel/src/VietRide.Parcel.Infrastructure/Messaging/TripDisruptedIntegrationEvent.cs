using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Parcel.Infrastructure.Messaging;

public sealed record TripDisruptedIntegrationEvent : IIntegrationEvent
{
    public const string EventType = "trip.trip.disrupted";

    public Guid TripId { get; init; }
    public bool HasSubstitution { get; init; }
    public decimal TraveledRatio { get; init; }
    public Guid EventId { get; init; }
    public DateTime OccurredAt { get; init; }

    [JsonIgnore]
    string IIntegrationEvent.EventType => EventType;
}
