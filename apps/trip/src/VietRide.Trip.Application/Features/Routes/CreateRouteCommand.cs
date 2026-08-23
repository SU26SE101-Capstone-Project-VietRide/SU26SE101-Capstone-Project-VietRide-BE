using MediatR;

namespace VietRide.Trip.Application.Features.Routes;

public sealed record CreateRouteCommand(
    Guid OperatorId,
    string? Name,
    Guid OriginStationId,
    Guid DestinationStationId,
    Guid? ReturnRouteId,
    long BaseFare,
    decimal? TotalDistanceKm,
    int? EstimatedDurationMinutes,
    bool? IsActive,
    string? Code = null) : IRequest<RouteDto>;
