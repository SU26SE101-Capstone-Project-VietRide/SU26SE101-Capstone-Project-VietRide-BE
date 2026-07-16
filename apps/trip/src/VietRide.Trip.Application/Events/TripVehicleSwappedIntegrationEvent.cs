using System.Text.Json.Serialization;
using VietRide.Shared.Messaging.Abstractions;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Events;

public sealed class TripVehicleSwappedIntegrationEvent : IntegrationEventBase
{
    public const string EventTypeValue = "trip.trip.vehicle_swapped";

    public TripVehicleSwappedIntegrationEvent(
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid tripId,
        Guid operatorId,
        Guid oldVehicleId,
        Guid newVehicleId,
        string oldVehiclePlateNumber,
        string newVehiclePlateNumber,
        DateTimeOffset departureDateTime,
        Guid driverUserId,
        Guid? assistantUserId,
        IReadOnlyCollection<VehicleSwapBookingSeatImpact> seatImpacts)
        : base(eventId, occurredAt.UtcDateTime)
    {
        TripId = tripId;
        OperatorId = operatorId;
        OldVehicleId = oldVehicleId;
        NewVehicleId = newVehicleId;
        OldVehiclePlateNumber = oldVehiclePlateNumber;
        NewVehiclePlateNumber = newVehiclePlateNumber;
        DepartureDateTime = departureDateTime;
        DriverUserId = driverUserId;
        AssistantUserId = assistantUserId;
        SeatImpacts = seatImpacts;
    }

    public Guid TripId { get; }

    public Guid OperatorId { get; }

    public Guid OldVehicleId { get; }

    public Guid NewVehicleId { get; }

    public string OldVehiclePlateNumber { get; }

    public string NewVehiclePlateNumber { get; }

    public DateTimeOffset DepartureDateTime { get; }

    public Guid DriverUserId { get; }

    public Guid? AssistantUserId { get; }

    public IReadOnlyCollection<VehicleSwapBookingSeatImpact> SeatImpacts { get; }

    [JsonIgnore]
    public override string EventType => EventTypeValue;
}
