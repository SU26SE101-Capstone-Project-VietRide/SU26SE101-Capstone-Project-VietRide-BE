using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Trip.Application.Features.Routes;

[SkipTransaction]
public sealed record UpsertFullRouteCommand(
    Guid OperatorId,
    Guid ActorUserId,
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
    IReadOnlyList<FullRouteStopInput> Stops,
    string? Code = null) : IRequest<RouteDto>;
