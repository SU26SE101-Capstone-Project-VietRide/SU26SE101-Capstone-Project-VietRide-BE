using MediatR;

namespace VietRide.Trip.Application.Features.AlternativeRoutes;

public sealed record GetAlternativeRouteQuery(
    Guid OperatorId,
    Guid AlternativeRouteId) : IRequest<AlternativeRouteDto>;
