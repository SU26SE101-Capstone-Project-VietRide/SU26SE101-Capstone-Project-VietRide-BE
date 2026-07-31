namespace VietRide.Trip.Application.Abstractions.Repositories;

public sealed record OperatorRoutePerformanceReadModel(
    Guid RouteId,
    string RouteName,
    string OriginName,
    string DestinationName,
    int TripCount,
    int CompletedTripCount);
