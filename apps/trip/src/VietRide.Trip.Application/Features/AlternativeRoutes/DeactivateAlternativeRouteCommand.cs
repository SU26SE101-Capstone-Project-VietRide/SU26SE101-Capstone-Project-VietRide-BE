using MediatR;

namespace VietRide.Trip.Application.Features.AlternativeRoutes;

public sealed record DeactivateAlternativeRouteCommand(
    Guid OperatorId,
    Guid AlternativeRouteId) : IRequest<Unit>;
