namespace VietRide.Trip.Application.Features.Routes;

using VietRide.Trip.Application.Features.Stations;

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
    string? PathPolyline,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    StationDto? OriginStation = null,
    StationDto? DestinationStation = null);
