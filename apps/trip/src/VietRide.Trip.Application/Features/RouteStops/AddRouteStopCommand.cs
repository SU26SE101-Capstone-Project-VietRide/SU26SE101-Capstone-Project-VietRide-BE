using MediatR;

namespace VietRide.Trip.Application.Features.RouteStops;

public sealed record AddRouteStopCommand(
    Guid OperatorId,
    Guid RouteId,
    Guid StopId,
    int OrderIndex,
    int EstimatedDurationFromOriginMinutes,
    decimal? DistanceFromOriginKm,
    bool AllowPickup,
    bool AllowDropoff) : IRequest<RouteStopDto>;
