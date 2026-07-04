using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Parcel.Infrastructure.Messaging;

public sealed record TripVehicleSubstitutedIntegrationEvent : IIntegrationEvent
{
    public const string EventType = "trip.vehicle_substituted";

    public Guid OldTripId { get; init; }
    public Guid NewTripId { get; init; }
    public Guid OperatorId { get; init; }
    public string? Reason { get; init; }
    public Guid EventId { get; init; }
    public DateTime OccurredAt { get; init; }

    [JsonIgnore]
    string IIntegrationEvent.EventType => EventType;
}
