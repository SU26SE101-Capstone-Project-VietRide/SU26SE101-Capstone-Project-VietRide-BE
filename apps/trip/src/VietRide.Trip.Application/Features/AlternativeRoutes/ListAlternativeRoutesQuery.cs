using MediatR;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Application.Features.AlternativeRoutes;

public sealed record ListAlternativeRoutesQuery(
    Guid OperatorId,
    Guid RouteId,
    int? Page,
    int? PageSize) : IRequest<PagedResult<AlternativeRouteListItemDto>>;
