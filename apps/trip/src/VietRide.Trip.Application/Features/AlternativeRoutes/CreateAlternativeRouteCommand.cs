using MediatR;

namespace VietRide.Trip.Application.Features.AlternativeRoutes;

public sealed record CreateAlternativeRouteCommand(
    Guid OperatorId,
    Guid RouteId,
    string? Name,
    string? Description,
    Guid DestinationStationId,
    decimal? TotalDistanceKm,
    int? EstimatedDurationMinutes,
    IReadOnlyList<AlternativeRouteStopInput> Stops) : IRequest<AlternativeRouteDto>;
