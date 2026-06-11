namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record AlternativeRouteStopRequest(
    Guid StopId,
    int OrderIndex,
    int EstimatedDurationFromOriginMinutes,
    decimal? DistanceFromOriginKm);
