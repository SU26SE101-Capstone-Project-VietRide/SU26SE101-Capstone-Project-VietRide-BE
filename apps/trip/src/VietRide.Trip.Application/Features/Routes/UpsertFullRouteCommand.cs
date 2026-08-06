using MediatR;

namespace VietRide.Trip.Application.Features.Routes;

public sealed record UpsertFullRouteCommand(
    Guid OperatorId,
    Guid? RouteId,
    string? Name,
    Guid OriginStationId,
    Guid DestinationStationId,
    Guid? ReturnRouteId,
    long BaseFare,
    bool? IsActive,
    string? PathPolyline,
    decimal? ManualDistanceKm,
    int? ManualDurationMinutes,
    IReadOnlyList<FullRouteStopInput> Stops) : IRequest<RouteDto>;
