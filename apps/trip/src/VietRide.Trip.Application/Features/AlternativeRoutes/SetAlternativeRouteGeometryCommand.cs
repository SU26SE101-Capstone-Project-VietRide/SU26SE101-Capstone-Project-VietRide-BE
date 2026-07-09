using MediatR;

namespace VietRide.Trip.Application.Features.AlternativeRoutes;

public sealed record SetAlternativeRouteGeometryCommand(
    Guid OperatorId,
    Guid AlternativeRouteId,
    string? PathPolyline) : IRequest<AlternativeRouteDto>;
