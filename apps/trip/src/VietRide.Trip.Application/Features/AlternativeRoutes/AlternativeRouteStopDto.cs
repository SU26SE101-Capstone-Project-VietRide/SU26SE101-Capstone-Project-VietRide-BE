namespace VietRide.Trip.Application.Features.AlternativeRoutes;

public sealed record AlternativeRouteStopDto(
    Guid AlternativeRouteId,
    Guid StopId,
    int OrderIndex,
    int EstimatedDurationFromOriginMinutes,
    decimal? DistanceFromOriginKm,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
