using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Trip.Application.Events;

public sealed class ShuttleUnassignedIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "trip.shuttle.unassigned";

    public ShuttleUnassignedIntegrationEvent(
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid shuttleTripId,
        Guid mainTripId,
        Guid operatorId,
        Guid actorUserId,
        Guid bookingId,
        string direction,
        DriverSnapshot driver,
        string reason,
        int remainingPassengerCount,
        bool shuttleTripCancelled,
        IReadOnlyCollection<PassengerRecipient> passengers)
        : base(eventId, occurredAt.UtcDateTime)
    {
        ShuttleTripId = shuttleTripId;
        MainTripId = mainTripId;
        OperatorId = operatorId;
        ActorUserId = actorUserId;
        BookingId = bookingId;
        Direction = direction;
        Driver = driver;
        Reason = reason;
        RemainingPassengerCount = remainingPassengerCount;
        ShuttleTripCancelled = shuttleTripCancelled;
        Passengers = passengers;
    }

    public Guid ShuttleTripId { get; }
    public Guid MainTripId { get; }
    public Guid OperatorId { get; }
    public Guid ActorUserId { get; }
    public Guid BookingId { get; }
    public string Direction { get; }
    public DriverSnapshot Driver { get; }
    public string Reason { get; }
    public int RemainingPassengerCount { get; }
    public bool ShuttleTripCancelled { get; }
    public IReadOnlyCollection<PassengerRecipient> Passengers { get; }

    [JsonIgnore]
    public override string EventType => EventTypeValue;

    public sealed record DriverSnapshot(Guid UserId);

    public sealed record PassengerRecipient(
        Guid PassengerUserId,
        IReadOnlyCollection<Guid> TicketIds);
}
