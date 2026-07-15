using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TripScheduleChangedIntegrationEvent : IIntegrationEvent
{
    public const string EventTypeValue = "trip.trip.schedule_changed";

    [JsonRequired]
    public Guid EventId { get; init; }
    [JsonRequired]
    public DateTime OccurredAt { get; init; }
    [JsonRequired]
    public Guid TripId { get; init; }
    [JsonRequired]
    public Guid OperatorId { get; init; }
    [JsonRequired]
    public DateTimeOffset OldDeparture { get; init; }
    [JsonRequired]
    public DateTimeOffset NewDeparture { get; init; }
    [JsonRequired]
    public string Severity { get; init; } = string.Empty;

    [JsonIgnore]
    string IIntegrationEvent.EventType => EventTypeValue;
}
