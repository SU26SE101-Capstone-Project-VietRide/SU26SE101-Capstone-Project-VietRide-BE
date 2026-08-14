using MediatR;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Application.Features.Routes;

public sealed record ListRoutesQuery(
    Guid OperatorId,
    int? Page,
    int? PageSize,
    string? Search,
    bool? IsActive = null,
    Guid? OriginStationId = null,
    Guid? DestinationStationId = null,
    string? SortBy = null,
    string? SortDir = null) : IRequest<PagedResult<RouteListItemDto>>;
