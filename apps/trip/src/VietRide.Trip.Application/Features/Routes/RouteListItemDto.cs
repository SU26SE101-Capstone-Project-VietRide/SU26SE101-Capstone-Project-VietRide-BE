namespace VietRide.Trip.Application.Features.Routes;

using VietRide.Trip.Application.Features.Stations;

public sealed record RouteListItemDto(
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
    DateTimeOffset UpdatedAt,
    StationDto? OriginStation = null,
    StationDto? DestinationStation = null);
