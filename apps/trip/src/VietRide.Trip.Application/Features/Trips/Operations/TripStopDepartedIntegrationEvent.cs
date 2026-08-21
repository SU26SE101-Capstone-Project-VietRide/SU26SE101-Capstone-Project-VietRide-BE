using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed class TripStopDepartedIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "trip.stop.departed";

    public TripStopDepartedIntegrationEvent(
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid tripId,
        Guid stopId,
        Guid operatorId,
        DateTimeOffset departedAt)
        : base(eventId, occurredAt.UtcDateTime)
    {
        TripId = tripId;
        StopId = stopId;
        OperatorId = operatorId;
        DepartedAt = departedAt.ToUniversalTime();
    }

    public override string EventType => EventTypeValue;

    public Guid TripId { get; }
    public Guid StopId { get; }
    public Guid OperatorId { get; }
    public DateTimeOffset DepartedAt { get; }
}
