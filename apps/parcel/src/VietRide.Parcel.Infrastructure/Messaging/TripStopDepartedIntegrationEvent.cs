using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Parcel.Infrastructure.Messaging;

public sealed record TripStopDepartedIntegrationEvent : IIntegrationEvent
{
    public const string EventTypeValue = "trip.stop.departed";

    public Guid EventId { get; init; }
    public DateTime OccurredAt { get; init; }
    public Guid TripId { get; init; }
    public Guid StopId { get; init; }
    public Guid OperatorId { get; init; }
    public DateTimeOffset DepartedAt { get; init; }

    [JsonIgnore]
    string IIntegrationEvent.EventType => EventTypeValue;
}
