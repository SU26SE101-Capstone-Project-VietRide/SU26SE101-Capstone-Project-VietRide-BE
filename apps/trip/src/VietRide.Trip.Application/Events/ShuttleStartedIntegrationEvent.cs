using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Trip.Application.Events;

public sealed class ShuttleStartedIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "trip.shuttle.started";

    public ShuttleStartedIntegrationEvent(
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid shuttleTripId,
        Guid mainTripId,
        Guid operatorId,
        Guid driverUserId,
        string direction,
        IReadOnlyCollection<PassengerRecipient> passengers)
        : base(eventId, occurredAt.UtcDateTime)
    {
        ShuttleTripId = shuttleTripId;
        MainTripId = mainTripId;
        OperatorId = operatorId;
        DriverUserId = driverUserId;
        Direction = direction;
        Passengers = passengers;
    }

    public Guid ShuttleTripId { get; }
    public Guid MainTripId { get; }
    public Guid OperatorId { get; }
    public Guid DriverUserId { get; }
    public string Direction { get; }
    public IReadOnlyCollection<PassengerRecipient> Passengers { get; }

    [JsonIgnore]
    public override string EventType => EventTypeValue;

    public sealed record PassengerRecipient(
        Guid PassengerUserId,
        Guid? BookingId,
        int PickupOrder);
}
