using MediatR;

namespace VietRide.Trip.Application.Features.AlternativeRoutes;

public sealed record UpdateAlternativeRouteCommand(
    Guid OperatorId,
    Guid AlternativeRouteId,
    string? Name,
    bool HasName,
    string? Description,
    bool HasDescription,
    Guid? DestinationStationId,
    bool HasDestinationStationId,
    decimal? TotalDistanceKm,
    bool HasTotalDistanceKm,
    int? EstimatedDurationMinutes,
    bool HasEstimatedDurationMinutes,
    bool? IsActive,
    bool HasStops,
    IReadOnlyList<AlternativeRouteStopInput>? Stops) : IRequest<AlternativeRouteDto>;
