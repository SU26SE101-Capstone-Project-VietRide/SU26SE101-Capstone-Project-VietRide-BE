using MediatR;

namespace VietRide.Trip.Application.Features.Routes;

public sealed record UpdateRouteCommand(
    Guid OperatorId,
    Guid RouteId,
    string? Name,
    Guid? ReturnRouteId,
    long? BaseFare,
    decimal? TotalDistanceKm,
    int? EstimatedDurationMinutes,
    bool? IsActive) : IRequest<RouteDto>;
