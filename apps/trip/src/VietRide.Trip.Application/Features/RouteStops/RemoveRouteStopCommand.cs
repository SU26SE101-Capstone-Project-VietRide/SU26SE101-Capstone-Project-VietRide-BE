using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Trip.Application.Features.RouteStops;

[SkipTransaction]
public sealed record RemoveRouteStopCommand(
    Guid OperatorId,
    Guid ActorUserId,
    Guid RouteId,
    Guid StopId) : IRequest<Unit>;
