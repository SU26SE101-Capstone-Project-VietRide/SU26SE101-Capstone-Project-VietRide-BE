using MediatR;

namespace VietRide.Trip.Application.Features.RouteStopFareTemplates;

public sealed record CreateRouteStopFareTemplateCommand(
    Guid OperatorId,
    Guid RouteId,
    Guid StopId,
    long FareFromThisStop,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveUntil) : IRequest<RouteStopFareTemplateDto>;
