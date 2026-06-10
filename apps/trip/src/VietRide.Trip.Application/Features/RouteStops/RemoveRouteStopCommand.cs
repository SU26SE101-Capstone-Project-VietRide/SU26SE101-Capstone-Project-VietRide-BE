using MediatR;

namespace VietRide.Trip.Application.Features.RouteStops;

public sealed record RemoveRouteStopCommand(
    Guid OperatorId,
    Guid RouteId,
    Guid StopId) : IRequest<Unit>;
