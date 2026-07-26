using MediatR;

namespace VietRide.Trip.Application.Features.Internal.Routes;

public sealed record GetRouteOwnershipQuery(Guid RouteId, Guid OperatorId) : IRequest<RouteOwnershipDto>;
