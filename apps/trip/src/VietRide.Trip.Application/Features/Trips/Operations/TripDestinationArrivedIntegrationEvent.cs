using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed class TripDestinationArrivedIntegrationEvent : IntegrationEventBase
{
    public TripDestinationArrivedIntegrationEvent(
        Guid tripId,
        Guid destinationStationId,
        Guid operatorId,
        Guid actorUserId,
        DateTimeOffset actualArrivalTime)
        : base(Guid.NewGuid(), actualArrivalTime.UtcDateTime)
    {
        TripId = tripId;
        DestinationStationId = destinationStationId;
        OperatorId = operatorId;
        ActorUserId = actorUserId;
        ActualArrivalTime = actualArrivalTime;
    }

    public override string EventType => "trip.destination.arrived";

    public Guid TripId { get; }
    public Guid DestinationStationId { get; }
    public Guid OperatorId { get; }
    public Guid ActorUserId { get; }
    public DateTimeOffset ActualArrivalTime { get; }
}
