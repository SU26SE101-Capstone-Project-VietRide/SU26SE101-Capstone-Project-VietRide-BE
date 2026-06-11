namespace VietRide.Trip.Application.Features.RouteStops;

public sealed record RouteStopDto(
    Guid RouteId,
    Guid StopId,
    int OrderIndex,
    int EstimatedDurationFromOriginMinutes,
    decimal? DistanceFromOriginKm,
    bool AllowPickup,
    bool AllowDropoff,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
