using MediatR;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Application.Features.Stops;

public sealed record ListStopsQuery(
    Guid OperatorId,
    int? Page,
    int? PageSize,
    string? Search,
    bool? IsActive = null,
    Guid? RouteId = null) : IRequest<PagedResult<StopDto>>;
