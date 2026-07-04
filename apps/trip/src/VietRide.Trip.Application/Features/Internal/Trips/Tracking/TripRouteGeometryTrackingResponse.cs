namespace VietRide.Trip.Application.Features.Internal.Trips.Tracking;

public sealed record TripRouteGeometryTrackingResponse(
    Guid TripId,
    IReadOnlyList<RouteGeometryPointDto> Points,
    IReadOnlyList<Guid>? AlertRecipientUserIds);
