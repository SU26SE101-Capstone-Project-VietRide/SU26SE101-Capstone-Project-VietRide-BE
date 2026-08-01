namespace VietRide.Trip.Application.Features.Internal.OperatorAnalytics;

public sealed record OperatorRoutePerformanceResponse(
    Guid RouteId,
    string RouteName,
    string OriginName,
    string DestinationName,
    int TripCount,
    int CompletedTripCount);
