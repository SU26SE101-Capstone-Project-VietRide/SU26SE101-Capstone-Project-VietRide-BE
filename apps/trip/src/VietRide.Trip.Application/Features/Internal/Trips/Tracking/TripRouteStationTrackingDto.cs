namespace VietRide.Trip.Application.Features.Internal.Trips.Tracking;

public sealed record TripRouteStationTrackingDto(
    Guid StationId,
    string Name,
    double Latitude,
    double Longitude);
