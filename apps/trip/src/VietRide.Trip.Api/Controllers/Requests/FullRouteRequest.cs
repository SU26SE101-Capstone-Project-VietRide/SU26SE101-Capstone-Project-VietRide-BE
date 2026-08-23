namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record FullRouteRequest(
    string? Name,
    Guid OriginStationId,
    Guid DestinationStationId,
    Guid? ReturnRouteId,
    long BaseFare,
    bool? IsActive,
    string? PathPolyline,
    RouteManualMetricsRequest? ManualMetrics,
    IReadOnlyList<FullRouteStopRequest>? Stops,
    string? Code = null);
