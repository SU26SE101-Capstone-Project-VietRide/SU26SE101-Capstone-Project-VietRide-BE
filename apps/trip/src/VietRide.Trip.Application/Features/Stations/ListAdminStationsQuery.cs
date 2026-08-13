using MediatR;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Application.Features.Stations;

public sealed record ListAdminStationsQuery(
    int? Page,
    int? PageSize,
    string? Search,
    bool? IsActive,
    bool? SupportsShuttle = null,
    string? SortBy = null,
    string? SortDir = null)
    : IRequest<PagedResult<StationDto>>;
