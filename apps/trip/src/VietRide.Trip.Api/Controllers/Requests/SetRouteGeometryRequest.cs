namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record SetRouteGeometryRequest(string? PathPolyline, RouteManualMetricsRequest? ManualMetrics = null);
