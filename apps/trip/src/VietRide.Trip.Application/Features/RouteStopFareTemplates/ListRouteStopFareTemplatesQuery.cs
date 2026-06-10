using MediatR;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Application.Features.RouteStopFareTemplates;

public sealed record ListRouteStopFareTemplatesQuery(
    Guid OperatorId,
    Guid RouteId,
    int? Page,
    int? PageSize) : IRequest<PagedResult<RouteStopFareTemplateDto>>;
