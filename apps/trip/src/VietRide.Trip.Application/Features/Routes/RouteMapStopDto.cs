namespace VietRide.Trip.Application.Features.Routes;

public sealed record RouteMapStopDto(
    Guid RouteId,
    Guid StopId,
    int OrderIndex,
    int EstimatedDurationFromOriginMinutes,
    decimal? DistanceFromOriginKm,
    bool AllowPickup,
    bool AllowDropoff,
    string Name,
    string? Address,
    decimal Latitude,
    decimal Longitude,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
