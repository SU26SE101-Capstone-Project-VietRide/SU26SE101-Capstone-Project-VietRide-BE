namespace VietRide.Trip.Application.Events;

public sealed record TripVehicleSubstitutedIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid SubstitutionId,
    DateTimeOffset DisruptedAt,
    Guid OperatorId,
    Guid OldTripId,
    string OldTripStatus,
    Guid OldVehicleId,
    Guid NewTripId,
    string NewTripStatus,
    Guid NewVehicleId,
    string NewVehiclePlateNumber,
    DateTimeOffset NewTripDepartureDateTime,
    Guid ActorUserId,
    string Reason,
    bool NotifyPassengers,
    IReadOnlyList<TripVehicleSubstitutedIntegrationEvent.Mapping> Mappings)
{
    public const string EventType = "trip.trip.vehicle_substituted";

    public sealed record Mapping(
        Guid BookingId,
        Guid PassengerId,
        string? OriginalSeatNumber,
        string? NewSeatNumber,
        string OriginalBoardingStatus,
        string? OriginalSeatType = null,
        string? NewSeatType = null,
        bool IsSeatDowngrade = false);
}
