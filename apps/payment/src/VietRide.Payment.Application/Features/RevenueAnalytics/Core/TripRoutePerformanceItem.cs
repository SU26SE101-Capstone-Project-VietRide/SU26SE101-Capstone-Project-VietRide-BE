namespace VietRide.Payment.Application.Features.RevenueAnalytics.Core;

public sealed record TripRoutePerformanceItem(
    Guid RouteId,
    string RouteName,
    string OriginName,
    string DestinationName,
    int TripCount,
    int CompletedTripCount);
