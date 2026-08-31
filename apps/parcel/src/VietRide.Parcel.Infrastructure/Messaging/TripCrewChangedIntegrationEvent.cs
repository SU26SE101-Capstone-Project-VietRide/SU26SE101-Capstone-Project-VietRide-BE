using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Parcel.Infrastructure.Messaging;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TripCrewChangedIntegrationEvent : IIntegrationEvent
{
    public const string EventTypeValue = "trip.trip.crew_changed";

    [JsonRequired]
    public Guid EventId { get; init; }
    [JsonRequired]
    public Guid TripId { get; init; }
    [JsonRequired]
    public Guid OperatorId { get; init; }
    [JsonRequired]
    public Guid? OldDriverUserId { get; init; }
    public Guid? OldAssistantUserId { get; init; }
    [JsonRequired]
    public Guid? DriverUserId { get; init; }
    public Guid? AssistantUserId { get; init; }
    [JsonRequired]
    public string RouteName { get; init; } = string.Empty;
    public string? VehiclePlateNumber { get; init; }
    [JsonRequired]
    public DateTimeOffset DepartureDateTime { get; init; }

    [JsonIgnore]
    DateTime IIntegrationEvent.OccurredAt => DepartureDateTime.UtcDateTime;

    [JsonIgnore]
    string IIntegrationEvent.EventType => EventTypeValue;

}
