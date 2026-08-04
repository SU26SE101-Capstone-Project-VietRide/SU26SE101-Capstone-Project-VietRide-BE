namespace VietRide.Trip.Application.Features.RouteChangeProposals;

public sealed record RouteChangeProposalStopSnapshot(
    Guid StopId,
    int OrderIndex,
    int EstimatedDurationFromOriginMinutes,
    decimal? DistanceFromOriginKm);
