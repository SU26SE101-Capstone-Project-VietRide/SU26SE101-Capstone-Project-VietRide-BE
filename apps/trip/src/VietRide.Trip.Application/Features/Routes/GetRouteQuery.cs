using MediatR;

namespace VietRide.Trip.Application.Features.Routes;

public sealed record GetRouteQuery(Guid OperatorId, Guid RouteId) : IRequest<RouteDto>;
