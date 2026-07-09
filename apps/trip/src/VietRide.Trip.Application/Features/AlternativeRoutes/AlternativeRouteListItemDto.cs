namespace VietRide.Trip.Application.Features.AlternativeRoutes;

public sealed record AlternativeRouteListItemDto(
    Guid Id,
    Guid RouteId,
    string Name,
    string? Description,
    Guid DestinationStationId,
    decimal? TotalDistanceKm,
    int? EstimatedDurationMinutes,
    bool IsActive,
    IReadOnlyList<AlternativeRouteStopDto> Stops,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
