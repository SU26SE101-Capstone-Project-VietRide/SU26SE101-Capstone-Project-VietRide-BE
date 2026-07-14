using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

public sealed record TripCompletedIntegrationEvent : IIntegrationEvent
{
    public const string EventType = "trip.trip.completed";

    public Guid TripId { get; init; }

    public DateTimeOffset CompletedAt { get; init; }

    public bool HasSubstitution { get; init; }

    [JsonIgnore]
    public Guid EventId => TripId;

    [JsonIgnore]
    public DateTime OccurredAt => CompletedAt.UtcDateTime;

    [JsonIgnore]
    string IIntegrationEvent.EventType => EventType;
}
