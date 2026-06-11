namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record CreateAlternativeRouteRequest(
    string? Name,
    string? Description,
    Guid DestinationStationId,
    decimal? TotalDistanceKm,
    int? EstimatedDurationMinutes,
    IReadOnlyList<AlternativeRouteStopRequest> Stops);
