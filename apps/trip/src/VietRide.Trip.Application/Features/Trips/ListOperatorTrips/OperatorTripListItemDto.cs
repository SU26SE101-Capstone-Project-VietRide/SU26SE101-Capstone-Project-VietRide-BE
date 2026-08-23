namespace VietRide.Trip.Application.Features.Trips.ListOperatorTrips;

public sealed record OperatorTripListItemDto(
    Guid TripId,
    string Status,
    OperatorTripRouteDto Route,
    OperatorTripVehicleDto Vehicle,
    OperatorTripCrewDto? Driver,
    OperatorTripCrewDto? Assistant,
    DateTimeOffset DepartureAt,
    DateTimeOffset ArrivalEstimate,
    bool CanSubstituteVehicle,
    Guid? SourceScheduleId = null,
    string? TripCode = null);
