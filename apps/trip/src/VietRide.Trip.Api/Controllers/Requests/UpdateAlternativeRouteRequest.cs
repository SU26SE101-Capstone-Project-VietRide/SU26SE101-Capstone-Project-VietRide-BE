namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record UpdateAlternativeRouteRequest(
    string? Name,
    string? Description,
    Guid DestinationStationId,
    decimal? TotalDistanceKm,
    int? EstimatedDurationMinutes,
    bool? IsActive,
    IReadOnlyList<AlternativeRouteStopRequest> Stops);
