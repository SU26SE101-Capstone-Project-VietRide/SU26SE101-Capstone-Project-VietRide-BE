namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record UpdateRouteRequest(
    string? Name,
    Guid? ReturnRouteId,
    long? BaseFare,
    decimal? TotalDistanceKm,
    int? EstimatedDurationMinutes,
    bool? IsActive);
