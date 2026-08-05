namespace VietRide.Trip.Application.Features.Routes;

public sealed record FullRouteStopInput(
    Guid StopId,
    int OrderIndex,
    int? EstimatedDurationFromOriginMinutes,
    decimal? DistanceFromOriginKm,
    bool AllowPickup,
    bool AllowDropoff);
