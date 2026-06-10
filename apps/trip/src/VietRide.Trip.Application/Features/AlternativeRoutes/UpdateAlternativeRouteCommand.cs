using MediatR;

namespace VietRide.Trip.Application.Features.AlternativeRoutes;

public sealed record UpdateAlternativeRouteCommand(
    Guid OperatorId,
    Guid AlternativeRouteId,
    string? Name,
    string? Description,
    Guid DestinationStationId,
    decimal? TotalDistanceKm,
    int? EstimatedDurationMinutes,
    bool? IsActive,
    IReadOnlyList<AlternativeRouteStopInput> Stops) : IRequest<AlternativeRouteDto>;
