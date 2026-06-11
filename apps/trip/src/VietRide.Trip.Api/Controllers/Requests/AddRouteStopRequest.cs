namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record AddRouteStopRequest(
    Guid StopId,
    int OrderIndex,
    int EstimatedDurationFromOriginMinutes,
    decimal? DistanceFromOriginKm,
    bool? AllowPickup,
    bool? AllowDropoff);
