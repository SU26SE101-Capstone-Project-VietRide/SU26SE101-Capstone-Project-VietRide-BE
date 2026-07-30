using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Parcel.Infrastructure.Messaging;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TripDisruptedIntegrationEvent : IIntegrationEvent
{
    public const string EventType = "trip.trip.disrupted";

    [JsonRequired]
    public Guid EventId { get; init; }
    [JsonRequired]
    public DateTime OccurredAt { get; init; }
    [JsonRequired]
    public Guid TripId { get; init; }
    [JsonRequired]
    public Guid OperatorId { get; init; }
    [JsonRequired]
    public DateTimeOffset TerminalAt { get; init; }
    [JsonRequired]
    public bool HasSubstitution { get; init; }
    public string? Reason { get; init; }

    [JsonIgnore]
    string IIntegrationEvent.EventType => EventType;
}
