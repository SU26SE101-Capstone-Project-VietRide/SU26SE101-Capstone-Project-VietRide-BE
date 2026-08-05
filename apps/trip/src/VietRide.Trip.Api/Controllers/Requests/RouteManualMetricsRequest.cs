namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record RouteManualMetricsRequest(decimal TotalDistanceKm, int EstimatedDurationMinutes);
