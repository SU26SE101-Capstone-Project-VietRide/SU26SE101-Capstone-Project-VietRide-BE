using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Trip.Application.Features.RouteStops;

[SkipTransaction]
public sealed record AddRouteStopCommand(
    Guid OperatorId,
    Guid ActorUserId,
    Guid RouteId,
    Guid StopId,
    int OrderIndex,
    int EstimatedDurationFromOriginMinutes,
    decimal? DistanceFromOriginKm,
    bool AllowPickup,
    bool AllowDropoff) : IRequest<RouteStopDto>;
