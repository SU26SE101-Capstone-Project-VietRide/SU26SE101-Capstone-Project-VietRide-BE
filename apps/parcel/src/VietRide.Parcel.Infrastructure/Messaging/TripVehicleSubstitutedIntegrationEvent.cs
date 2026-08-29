using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Parcel.Infrastructure.Messaging;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TripVehicleSubstitutedIntegrationEvent : IIntegrationEvent
{
    public const string EventType = "trip.trip.vehicle_substituted";

    [JsonRequired]
    public Guid EventId { get; init; }
    [JsonRequired]
    public DateTimeOffset OccurredAt { get; init; }
    [JsonRequired]
    public Guid SubstitutionId { get; init; }
    [JsonRequired]
    public DateTimeOffset DisruptedAt { get; init; }
    [JsonRequired]
    public Guid OperatorId { get; init; }
    [JsonRequired]
    public Guid OldTripId { get; init; }
    [JsonRequired]
    public string OldTripStatus { get; init; } = string.Empty;
    [JsonRequired]
    public Guid OldVehicleId { get; init; }
    [JsonRequired]
    public Guid NewTripId { get; init; }
    [JsonRequired]
    public string NewTripStatus { get; init; } = string.Empty;
    [JsonRequired]
    public Guid NewVehicleId { get; init; }
    [JsonRequired]
    public string NewVehiclePlateNumber { get; init; } = string.Empty;
    [JsonRequired]
    public DateTimeOffset NewTripDepartureDateTime { get; init; }
    [JsonRequired]
    public Guid ActorUserId { get; init; }
    [JsonRequired]
    public string Reason { get; init; } = string.Empty;
    [JsonRequired]
    public bool NotifyPassengers { get; init; }
    [JsonRequired]
    public IReadOnlyList<TripVehicleSubstitutionMapping> Mappings { get; init; }
        = Array.Empty<TripVehicleSubstitutionMapping>();
    public Guid? IncidentId { get; init; }
    public decimal? IncidentLatitude { get; init; }
    public decimal? IncidentLongitude { get; init; }
    public string? IncidentDescription { get; init; }
    public Guid? NewDriverId { get; init; }
    public Guid? NewAssistantId { get; init; }

    [JsonIgnore]
    string IIntegrationEvent.EventType => EventType;

    [JsonIgnore]
    DateTime IIntegrationEvent.OccurredAt => OccurredAt.UtcDateTime;
}
