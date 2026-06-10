namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record CreateRouteRequest(
    string? Name,
    Guid OriginStationId,
    Guid DestinationStationId,
    Guid? ReturnRouteId,
    long BaseFare,
    decimal? TotalDistanceKm,
    int? EstimatedDurationMinutes,
    bool? IsActive);
