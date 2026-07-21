using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Booking.Infrastructure.Messaging;

public sealed class StopDisabledIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "trip.stop.disabled";

    public StopDisabledIntegrationEvent(Guid eventId, DateTimeOffset occurredAt, Guid stopId, Guid operatorId, Guid? replacedByStopId)
        : base(eventId, occurredAt.UtcDateTime)
    {
        StopId = stopId;
        OperatorId = operatorId;
        ReplacedByStopId = replacedByStopId;
    }

    public Guid StopId { get; init; }
    public Guid OperatorId { get; init; }
    public Guid? ReplacedByStopId { get; init; }

    public override string EventType => EventTypeValue;
}
