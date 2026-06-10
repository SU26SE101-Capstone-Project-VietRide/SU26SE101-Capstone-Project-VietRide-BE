using MediatR;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Application.Features.Routes;

public sealed record ListRoutesQuery(
    Guid OperatorId,
    int? Page,
    int? PageSize,
    string? Search) : IRequest<PagedResult<RouteDto>>;
