namespace VietRide.Trip.Application.Features.DriverTrips.GetAssignedTripRoute;

public sealed record DriverTripRouteDto(
    Guid TripId,
    Guid RouteId,
    string? PathPolyline,
    DriverTripRouteStationDto OriginStation,
    DriverTripRouteStationDto DestinationStation,
    IReadOnlyList<DriverTripRouteStopDto> Stops);
