namespace VietRide.Trip.Application.Features.Internal.Trips.Tracking;

public sealed record TripRouteIntermediateStopTrackingDto(
    Guid StopId,
    string Name,
    int Sequence,
    double Latitude,
    double Longitude);
