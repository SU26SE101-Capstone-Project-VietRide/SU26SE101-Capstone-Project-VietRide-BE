using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed class TripStopArrivedIntegrationEvent : IntegrationEventBase
{
    public TripStopArrivedIntegrationEvent(
        Guid tripId,
        Guid stopId,
        Guid operatorId,
        Guid actorUserId,
        DateTimeOffset actualArrivalTime)
        : base(Guid.NewGuid(), actualArrivalTime.UtcDateTime)
    {
        TripId = tripId;
        StopId = stopId;
        OperatorId = operatorId;
        ActorUserId = actorUserId;
        ActualArrivalTime = actualArrivalTime;
    }

    public override string EventType => "trip.stop.arrived";

    public Guid TripId { get; }
    public Guid StopId { get; }
    public Guid OperatorId { get; }
    public Guid ActorUserId { get; }
    public DateTimeOffset ActualArrivalTime { get; }
}
