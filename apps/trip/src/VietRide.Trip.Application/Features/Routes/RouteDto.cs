namespace VietRide.Trip.Application.Features.Routes;

public sealed record RouteDto(
    Guid Id,
    Guid OperatorId,
    string Name,
    Guid OriginStationId,
    Guid DestinationStationId,
    Guid? ReturnRouteId,
    long BaseFare,
    decimal? TotalDistanceKm,
    int? EstimatedDurationMinutes,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
