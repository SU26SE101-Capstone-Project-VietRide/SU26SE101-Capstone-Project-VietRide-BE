using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;

namespace VietRide.Trip.Application.Events;

public sealed class ShuttleReassignedIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "trip.shuttle.reassigned";

    public ShuttleReassignedIntegrationEvent(
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid shuttleTripId,
        Guid mainTripId,
        Guid operatorId,
        string direction,
        Guid oldDriverUserId,
        DriverSnapshot newDriver,
        VehicleSnapshot oldVehicle,
        VehicleSnapshot newVehicle,
        string reason,
        IReadOnlyCollection<PassengerRecipient> passengers)
        : base(eventId, occurredAt.UtcDateTime)
    {
        ShuttleTripId = shuttleTripId;
        MainTripId = mainTripId;
        OperatorId = operatorId;
        Direction = direction;
        OldDriverUserId = oldDriverUserId;
        NewDriver = newDriver;
        OldVehicle = oldVehicle;
        NewVehicle = newVehicle;
        Reason = reason;
        Passengers = passengers;
    }

    public Guid ShuttleTripId { get; }
    public Guid MainTripId { get; }
    public Guid OperatorId { get; }
    public string Direction { get; }
    public Guid OldDriverUserId { get; }
    public DriverSnapshot NewDriver { get; }
    public VehicleSnapshot OldVehicle { get; }
    public VehicleSnapshot NewVehicle { get; }
    public string Reason { get; }
    public IReadOnlyCollection<PassengerRecipient> Passengers { get; }

    [JsonIgnore]
    public override string EventType => EventTypeValue;

    public sealed record DriverSnapshot(
        Guid UserId,
        string? DisplayName,
        string? Phone);

    public sealed record VehicleSnapshot(Guid Id, string LicensePlate);

    public sealed record PassengerRecipient(
        Guid PassengerUserId,
        Guid? BookingId,
        int PickupOrder);
}
