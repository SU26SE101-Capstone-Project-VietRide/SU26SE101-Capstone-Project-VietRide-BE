namespace VietRide.Payment.Application.Features.RevenueAnalytics.Core;

public sealed record TripRevenueSummaryItem(
    Guid TripId,
    string Status,
    DateTimeOffset DepartureAt,
    Guid RouteId,
    string RouteName,
    string OriginName,
    string DestinationName);
