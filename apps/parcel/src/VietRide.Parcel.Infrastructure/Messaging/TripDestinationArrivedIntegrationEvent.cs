using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Parcel.Infrastructure.Messaging;

public sealed record TripDestinationArrivedIntegrationEvent : IIntegrationEvent
{
    public const string EventTypeValue = "trip.destination.arrived";

    public Guid EventId { get; init; }
    public DateTime OccurredAt { get; init; }
    public Guid TripId { get; init; }
    public Guid DestinationStationId { get; init; }
    public Guid OperatorId { get; init; }
    public DateTimeOffset ActualArrivalTime { get; init; }

    [JsonIgnore]
    string IIntegrationEvent.EventType => EventTypeValue;
}
