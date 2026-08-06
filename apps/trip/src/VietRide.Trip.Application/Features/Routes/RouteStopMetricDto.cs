namespace VietRide.Trip.Application.Features.Routes;

public sealed record RouteStopMetricDto(
    Guid StopId,
    string StopName,
    int OrderIndex,
    decimal? DistanceFromOriginKm,
    int EstimatedDurationFromOriginMinutes);
