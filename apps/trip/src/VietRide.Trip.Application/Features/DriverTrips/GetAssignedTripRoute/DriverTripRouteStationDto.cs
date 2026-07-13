namespace VietRide.Trip.Application.Features.DriverTrips.GetAssignedTripRoute;

public sealed record DriverTripRouteStationDto(
    Guid StationId,
    string Name,
    double? Latitude,
    double? Longitude);
