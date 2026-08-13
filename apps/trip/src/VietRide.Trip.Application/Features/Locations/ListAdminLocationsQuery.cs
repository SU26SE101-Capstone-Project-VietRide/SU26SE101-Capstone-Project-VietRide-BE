using MediatR;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Application.Features.Locations;

public sealed record ListAdminLocationsQuery(
    int? Page,
    int? PageSize,
    string? Search,
    bool? IsActive,
    string? Type = null,
    string? ParentCode = null) : IRequest<PagedResult<LocationDto>>;
