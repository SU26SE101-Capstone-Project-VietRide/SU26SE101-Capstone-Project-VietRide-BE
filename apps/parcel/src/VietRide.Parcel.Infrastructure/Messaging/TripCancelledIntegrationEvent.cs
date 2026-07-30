using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Parcel.Infrastructure.Messaging;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TripCancelledIntegrationEvent : IIntegrationEvent
{
    public const string EventType = "trip.trip.cancelled";

    [JsonRequired]
    public Guid EventId { get; init; }
    [JsonRequired]
    public DateTime OccurredAt { get; init; }
    [JsonRequired]
    public Guid TripId { get; init; }
    [JsonRequired]
    public Guid OperatorId { get; init; }
    [JsonRequired]
    public DateTimeOffset CancelledAt { get; init; }
    [JsonRequired]
    public string CancelReason { get; init; } = string.Empty;

    [JsonIgnore]
    string IIntegrationEvent.EventType => EventType;
}
