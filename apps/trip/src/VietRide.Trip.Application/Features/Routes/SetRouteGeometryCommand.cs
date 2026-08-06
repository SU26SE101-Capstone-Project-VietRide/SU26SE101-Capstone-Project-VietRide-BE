using MediatR;

namespace VietRide.Trip.Application.Features.Routes;

public sealed record SetRouteGeometryCommand(
    Guid OperatorId,
    Guid RouteId,
    string? PathPolyline,
    decimal? ManualDistanceKm = null,
    int? ManualDurationMinutes = null)
    : IRequest<RouteDto>;
