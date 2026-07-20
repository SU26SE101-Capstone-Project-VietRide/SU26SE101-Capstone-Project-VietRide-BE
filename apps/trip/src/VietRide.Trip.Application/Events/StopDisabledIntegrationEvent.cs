using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Trip.Application.Events;

public sealed class StopDisabledIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "trip.stop.disabled";

    public StopDisabledIntegrationEvent(
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid stopId,
        Guid operatorId,
        Guid? replacedByStopId)
        : base(eventId, occurredAt.UtcDateTime)
    {
        StopId = stopId;
        OperatorId = operatorId;
        ReplacedByStopId = replacedByStopId;
    }

    public Guid StopId { get; }
    public Guid OperatorId { get; }
    public Guid? ReplacedByStopId { get; }

    public override string EventType => EventTypeValue;
}
