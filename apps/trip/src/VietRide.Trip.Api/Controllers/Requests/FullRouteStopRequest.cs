namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record FullRouteStopRequest(
    Guid StopId,
    int OrderIndex,
    int? EstimatedDurationFromOriginMinutes,
    decimal? DistanceFromOriginKm,
    bool AllowPickup = true,
    bool AllowDropoff = true);
