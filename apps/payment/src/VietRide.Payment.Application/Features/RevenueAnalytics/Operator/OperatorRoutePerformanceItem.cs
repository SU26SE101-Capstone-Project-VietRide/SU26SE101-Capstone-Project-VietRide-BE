namespace VietRide.Payment.Application.Features.RevenueAnalytics.Operator;

public sealed record OperatorRoutePerformanceItem(
    Guid RouteId,
    string RouteName,
    string OriginName,
    string DestinationName,
    int TripCount,
    int CompletedTripCount,
    int BookingCount,
    int ParcelCount,
    long RevenueVnd,
    decimal CompletionRatePercent);
