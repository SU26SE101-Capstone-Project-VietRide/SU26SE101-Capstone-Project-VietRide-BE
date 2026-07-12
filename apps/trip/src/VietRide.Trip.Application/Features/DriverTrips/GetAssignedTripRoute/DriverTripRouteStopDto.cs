namespace VietRide.Trip.Application.Features.DriverTrips.GetAssignedTripRoute;

public sealed record DriverTripRouteStopDto(
    Guid StopId,
    string Name,
    double Latitude,
    double Longitude,
    int OrderIndex,
    DateTimeOffset EstimatedArrivalTime,
    bool AllowPickup,
    bool AllowDropoff);
