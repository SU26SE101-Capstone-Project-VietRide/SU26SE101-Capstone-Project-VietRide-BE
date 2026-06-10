namespace VietRide.Trip.Application.Features.AlternativeRoutes;

public sealed record AlternativeRouteStopInput(
    Guid StopId,
    int OrderIndex,
    int EstimatedDurationFromOriginMinutes,
    decimal? DistanceFromOriginKm);
