namespace VietRide.Trip.Application.Features.Internal.Trips.Tracking;

public sealed record TripRouteStopsTrackingResponse(
    IReadOnlyList<TripRouteStopTrackingDto> Stops);
